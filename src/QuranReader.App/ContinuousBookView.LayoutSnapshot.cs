using System.Globalization;
using Microsoft.UI.Xaml.Documents;

namespace QuranReader.App;

internal sealed partial class ContinuousBookView
{
    internal object[] TraceStableLayout() => VisibleLeaves().Select(leaf =>
    {
        var glyphs = new List<object>();
        var lineTops = new HashSet<double>();
        foreach (var span in leaf.Spans)
        {
            var run = runs.First(r => r.Span.Source.Id == span.Source.Id);
            foreach (var offset in StringInfo.ParseCombiningCharacters(span.Source.Text).Where(o => o >= span.Start && o < span.End))
            {
                var rect = run.Run.ContentStart.GetPositionAtOffset(offset - run.Span.Start, LogicalDirection.Forward)!.GetCharacterRect(LogicalDirection.Forward);
                lineTops.Add(Math.Round(rect.Top, 3));
                glyphs.Add(new { Id = span.Source.Id, Offset = offset, X = Math.Round(rect.X, 3), Y = Math.Round(rect.Y, 3), Width = Math.Round(rect.Width, 3), Height = Math.Round(rect.Height, 3), RawRect = rect });
            }
        }
        var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(glyphs);
        return (object)new
        {
            Identity = leaf.Label.Text,
            leaf.Start,
            leaf.End,
            Width = leaf.Surface.ActualWidth,
            Height = leaf.Surface.ActualHeight,
            FontScale = settings.Scale,
            Spans = leaf.Spans.Select(s => new { Id = s.Source.Id, s.Start, s.Length }).ToArray(),
            GlyphCount = glyphs.Count,
            LineCount = lineTops.Count,
            ContentWidth = leaf.Flow.ActualWidth,
            ContentHeight = leaf.Flow.ActualHeight,
            GeometrySha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)),
            Glyphs = glyphs,
            Headings = headings.Where(h => h.Inline.ContentStart.Offset >= NativeStart(leaf).Offset && h.Inline.ContentStart.Offset < (leaves.IndexOf(leaf) + 1 < leaves.Count ? NativeStart(leaves[leaves.IndexOf(leaf) + 1]).Offset : text!.ContentEnd.Offset)).Select(h => new { h.Surah, Rect = h.Inline.ContentStart.GetCharacterRect(LogicalDirection.Forward), h.Header.BasmalahHeight }).ToArray()
        };
    }).ToArray();

}
