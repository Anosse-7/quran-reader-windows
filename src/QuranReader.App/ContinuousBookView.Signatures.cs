using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.UI.Xaml.Documents;
using QuranReader.Core;

namespace QuranReader.App;

internal sealed partial class ContinuousBookView
{
    // Capture native geometry on its UI thread; hash the detached values on a worker.
    // Include exact source, every native line boundary and introduction. Full
    // per-grapheme and pixel comparisons additionally run in native verification.
    internal async Task<IReadOnlyList<string>> CaptureLayoutSignaturesAsync(CancellationToken cancellation = default)
    {
        var nativeRevision = revision;
        var slice = System.Diagnostics.Stopwatch.StartNew();
        var trace = NavigationTrace.Current;
        NavigationTrace.Mark(trace, "SignatureGeometryStarted");
        var values = new List<string>(leaves.Count);
        var nativeReads = 0; var reusedReads = 0;
        foreach (var leaf in leaves)
        {
            var data = new StringBuilder();
            data.Append(CultureInfo.InvariantCulture, $"{leaf.Surface.ActualWidth:R},{leaf.Surface.ActualHeight:R},{settings.Scale:R};");
            foreach (var span in leaf.Spans)
            {
                cancellation.ThrowIfCancellationRequested();
                if (nativeRevision != revision) throw new ResourceException("Native signature document changed during capture.");
                data.Append(CultureInfo.InvariantCulture, $"{span.Source.Id.Surah}:{span.Source.Id.Ayah}:{span.Start}:{span.Length}:");
                data.Append(span.Source.Text.AsSpan(span.Start, span.Length));
                var slot = runs.First(r => r.Span.Source.Id == span.Source.Id);
                var offsets = StringInfo.ParseCombiningCharacters(span.Source.Text).Where(o => o >= span.Start && o < span.End).ToArray();
                // The snapshot is synchronous on the UI thread: geometry cannot
                // change between these reads. Binary searches revisit offsets;
                // reuse their exact Rect values within this one span snapshot.
                // Never carry this memo across a layout or native document.
                var rectangles = new Dictionary<int, Windows.Foundation.Rect>();
                Windows.Foundation.Rect At(int i)
                {
                    if (rectangles.TryGetValue(i, out var saved)) { reusedReads++; return saved; }
                    var measured = slot.Run.ContentStart.GetPositionAtOffset(offsets[i] - slot.Span.Start, LogicalDirection.Forward)!.GetCharacterRect(LogicalDirection.Forward);
                    nativeReads++; rectangles.Add(i, measured); return measured;
                }
                for (var line = 0; line < offsets.Length;)
                {
                    var rect = At(line);
                    var low = line; var high = offsets.Length - 1;
                    while (low < high)
                    {
                        var mid = (low + high + 1) / 2;
                        if (Math.Round(At(mid).Top, 3) == Math.Round(rect.Top, 3)) low = mid; else high = mid - 1;
                    }
                    var lastRect = At(low);
                    data.Append(CultureInfo.InvariantCulture, $";{offsets[line]}:{offsets[low]},{Math.Round(rect.X, 3):R},{Math.Round(rect.Y, 3):R},{Math.Round(rect.Width, 3):R},{Math.Round(rect.Height, 3):R},{Math.Round(lastRect.X, 3):R},{Math.Round(lastRect.Y, 3):R},{Math.Round(lastRect.Width, 3):R},{Math.Round(lastRect.Height, 3):R}");
                    line = low + 1;
                }
                // Yield only between span snapshots. No rectangle memo survives
                // this dispatch boundary, and stale captures never commit.
                if (slice.ElapsedMilliseconds >= 8)
                {
                    var yielded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    if (!DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => yielded.TrySetResult()))
                        throw new ResourceException("The native signature queue is unavailable.");
                    await yielded.Task;
                    cancellation.ThrowIfCancellationRequested();
                    if (nativeRevision != revision) throw new ResourceException("Native signature document changed during capture.");
                    slice.Restart();
                }
            }
            var ordinal = leaves.IndexOf(leaf);
            var first = NativeStart(leaf).Offset;
            var last = ordinal + 1 < leaves.Count ? NativeStart(leaves[ordinal + 1]).Offset : text!.ContentEnd.Offset;
            foreach (var heading in headings.Where(h => h.Inline.ContentStart.Offset >= first && h.Inline.ContentStart.Offset < last))
            {
                var rect = heading.Inline.ContentStart.GetCharacterRect(LogicalDirection.Forward);
                data.Append(CultureInfo.InvariantCulture, $";H{heading.Surah},{rect.X:R},{rect.Y:R},{rect.Width:R},{rect.Height:R},{heading.Header.BasmalahHeight:R}");
            }
            values.Add(data.ToString());
        }
        if (trace != 0) NavigationTrace.Mark(trace, "SignatureGeometryCompleted", $"native-rect-reads={nativeReads}; repeated-reads-reused={reusedReads}");
        return await Task.Run<IReadOnlyList<string>>(() =>
        {
            NavigationTrace.Mark(trace, "SignatureHashStarted");
            var result = values.Select(v => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(v)))).ToArray();
            NavigationTrace.Mark(trace, "SignatureHashCompleted");
            return result;
        }, cancellation);
    }
}
