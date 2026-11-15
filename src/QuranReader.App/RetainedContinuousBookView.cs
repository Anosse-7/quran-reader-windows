using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QuranReader.Core;
using QuranReader.Data;
using Windows.Foundation;

namespace QuranReader.App;

/// <summary>Retained native presentation over one canonical, direction-independent leaf index per layout.</summary>
internal sealed class RetainedContinuousBookView : UserControl
{
    private readonly Grid layers = new();
    private readonly ContinuousBookView first = new();
    private readonly ContinuousBookView second = new();
    private readonly Dictionary<AdaptiveLayoutKey, AdaptivePaginationIndex> indexes = [];
    private readonly Queue<AdaptiveLayoutKey> indexOrder = [];
    private readonly HashSet<AdaptiveLayoutKey> checkpointLookups = [];
    private readonly HashSet<AdaptiveLayoutKey> restoredIndexes = [];
    private readonly Dictionary<AdaptiveLayoutKey, int> checkpointVersions = [];
    private readonly Dictionary<AdaptiveLayoutKey, int> savedCheckpointVersions = [];
    private readonly Dictionary<AdaptiveLayoutKey, Dictionary<int, ReadingPosition>> viewports = [];
    private readonly Dictionary<ContinuousBookView, AdaptiveLayoutKey> bankLayouts = [];
    private readonly Dictionary<ContinuousBookView, IReadOnlyList<ReadingLeaf>> bankMeasurements = [];
    private bool presentationActive = true;
    private ContinuousBookView current;
    private ContinuousBookView incoming;
    private ContinuousScripture? requested;
    private AdaptivePaginationIndex? index;
    private AdaptiveWindowBoundary? currentWindow;
    private readonly List<object> presentationFailures = [];
    private int revision;
    private bool preparing;
    private int? pendingLeaf;
    private double availableWidth, availableHeight;
    private ReaderSettings presentation = new();
    private ReaderPalette? latestPalette;
    private AyahId? latestSelection;
    private ReadingPosition pendingAnchor = AdaptivePaginationIndex.Origin;
    private readonly ReaderTranslationMotion motion = new();
    private TaskCompletionSource completion = new();
    private CancellationTokenSource? sourceCancellation;
    private readonly DispatcherTimer idleTimer = new() { Interval = TimeSpan.FromMilliseconds(650) };
    private bool idleActive;
    private bool idleForward = true;
    private bool idleEligible;
    internal Func<ReadingPosition, CancellationToken, Task<ContinuousScripture>>? ReadSourceAsync { get; set; }
    internal PaginationCheckpointStore? Checkpoints { get; set; }
    internal bool IsPreparing => preparing;
    internal int IdleCompletions { get; private set; }
    internal int IndexedWindowEnd => currentWindow is null ? 0 : currentWindow.FirstLeaf + currentWindow.Leaves.Count;
    internal bool AnimationsEnabled { get; set; } = true;
    internal AdaptiveLayoutKey? LayoutGeneration => index?.Layout;
    public bool Ready => CompositionError is null && !preparing && current.Ready;
    public string? CompositionError { get; private set; }
    public ContinuousScripture? Document => current.Document;
    public bool IsSpread => index?.Layout.Spread ?? current.IsSpread;
    public bool CanNext => (Ready || preparing && currentWindow is not null) && index is not null && ((pendingLeaf ?? VisibleLeaf) + (IsSpread ? 2 : 1) < index.Count || !index.Complete);
    public bool CanPrevious => Ready || preparing;
    public int VisibleLeaf => (currentWindow?.FirstLeaf ?? 0) + current.VisibleLeaf;
    public int TreeBuilds => first.TreeBuilds + second.TreeBuilds;
    public IReadOnlyList<ContinuousBookView.BookLeaf> Leaves => current.Leaves;
    public IReadOnlyList<ContinuousBookView.SourceRun> SourceRuns => current.SourceRuns;
    public event EventHandler<Verse>? AyahSelected;
    public event EventHandler? LayoutCommitted;
    public event EventHandler? PositionChanged;
    internal event EventHandler? PreparationChanged;
    public event EventHandler<BookWindowRequest>? WindowRequested;

    public RetainedContinuousBookView()
    {
        current = first; incoming = second; Content = layers;
        foreach (var engine in new[] { first, second })
        {
            layers.Children.Add(engine);
            engine.SetPresentationVisible(false);
            engine.AyahSelected += (sender, verse) => { if (ReferenceEquals(sender, current) && !preparing) AyahSelected?.Invoke(this, verse); };
            engine.PositionChanged += (sender, _) => { if (ReferenceEquals(sender, current) && !preparing) PositionChanged?.Invoke(this, EventArgs.Empty); };
        }
        completion.TrySetResult();
        idleTimer.Tick += (_, _) => PrepareIdle();
    }

    public void Show(ContinuousScripture source, ReaderSettings settings, AyahId? selected,
        ReaderPalette palette, double width, double height)
    {
        presentation = settings; latestPalette = palette; latestSelection = selected;
        current.UpdateAppearance(settings, selected, palette);
        if (settings.ReduceMotion || !AnimationsEnabled) motion.Stop();
        var spread = ReadingLayout.ShowSpread(width, settings.Scale);
        var key = new AdaptiveLayoutKey(Math.Round(Math.Max(280, Math.Min(spread ? 1540 : 780, width)), 1),
            Math.Round(Math.Max(settings.Scale * ReadingLayout.MushafLineHeightRatio * 3 + 88, height), 1),
            settings.Scale, XamlRoot?.RasterizationScale ?? 1, spread, ContentRepository.PinnedSha256, PublisherFont.PinnedSha256);
        if (ReferenceEquals(requested, source) && index?.Layout == key) return;
        var navigationTrace = NavigationTrace.Current;
        if (navigationTrace != 0) NavigationTrace.Mark(navigationTrace, "PaginationCacheLookup", indexes.ContainsKey(key) ? "layout-index-hit" : "layout-index-miss");
        var changedSource = !ReferenceEquals(requested, source);
        if (changedSource) { idleEligible = false; idleTimer.Stop(); }
        var anchor = changedSource ? source.Start : preparing ? pendingAnchor : current.CaptureAnchor();
        RememberViewport();
        requested = source; pendingAnchor = anchor; availableWidth = width; availableHeight = height;
        if (!indexes.TryGetValue(key, out var nextIndex))
        {
            if (indexes.Count == 8)
            {
                var obsolete = indexOrder.Dequeue(); indexes.Remove(obsolete); viewports.Remove(obsolete); checkpointLookups.Remove(obsolete); restoredIndexes.Remove(obsolete);
                checkpointVersions.Remove(obsolete); savedCheckpointVersions.Remove(obsolete);
            }
            nextIndex = new(key); indexes.Add(key, nextIndex); indexOrder.Enqueue(key);
            viewports.Add(key, []);
        }
        index = nextIndex;
        var cached = nextIndex.Find(anchor);
        if (current.Ready && cached == currentWindow && cached is not null &&
            bankLayouts.TryGetValue(current, out var retainedLayout) && retainedLayout == key)
        {
            sourceCancellation?.Cancel(); ++revision; completion.TrySetResult();
            preparing = false; pendingLeaf = null; motion.Stop();
            NavigationTrace.Mark(navigationTrace, "NativeBankCacheLookup", "current-window-hit");
            current.SetIndexedPresentation(cached.FirstLeaf, cached.Leaves.Count,
                nextIndex.Locate(cached, anchor) - cached.FirstLeaf, anchor);
            NavigationTrace.Incoming(navigationTrace);
            _ = NavigationTrace.AnimationAsync(navigationTrace, Task.CompletedTask);
            LayoutCommitted?.Invoke(this, EventArgs.Empty);
            return;
        }
        Begin(anchor, null, null);
    }

    internal void SetPresentationActive(bool active)
    {
        if (presentationActive == active) return;
        presentationActive = active;
        Opacity = active ? 1 : 0;
        IsHitTestVisible = active;
        current.SetPresentationVisible(active);
    }

    internal void Park()
    {
        RememberViewport();
        CancelPreparation();
        requested = null;
        SetPresentationActive(false);
    }

    internal void CancelPreparation()
    {
        idleTimer.Stop();
        if (idleActive) { sourceCancellation?.Cancel(); ++revision; idleActive = false; }
        if (!preparing) return;
        sourceCancellation?.Cancel(); ++revision; completion.TrySetResult();
        preparing = false; pendingLeaf = null; requested = null; motion.Stop();
        PreparationChanged?.Invoke(this, EventArgs.Empty);
    }

    internal bool CancelToVisible()
    {
        if (!preparing) return false;
        var anchor = current.Ready ? current.CaptureAnchor() : AdaptivePaginationIndex.Origin;
        CancelPreparation();
        WindowRequested?.Invoke(this, new(anchor, !current.Ready));
        return true;
    }

    private void Begin(ReadingPosition? anchor, int? leaf, bool? forward)
    {
        idleTimer.Stop(); idleActive = false;
        idleEligible = forward is not null;
        if (forward is { } direction) idleForward = direction;
        pendingLeaf = leaf;
        pendingAnchor = anchor ?? (leaf is { } ordinal && index?.Find(ordinal) is { } window
            ? window.Leaves.Single(l => l.Number == ordinal).Start : index?.NextSource ?? pendingAnchor);
        sourceCancellation?.Cancel(); sourceCancellation?.Dispose(); sourceCancellation = new();
        motion.Stop(); preparing = true; CompositionError = null;
        var serial = ++revision;
        completion.TrySetResult(); completion = new();
        PreparationChanged?.Invoke(this, EventArgs.Empty);
        PrepareAsync(serial, completion, index!, anchor, leaf, forward, sourceCancellation.Token);
    }

    private void ScheduleIdle()
    {
        idleTimer.Stop();
        if (!Ready || !presentationActive || !IsLoaded || currentWindow is null || !idleEligible) return;
        // Prepare only at the edge of the retained window, rather than spending
        // native work several spreads ahead while ordinary turns are active.
        var step = IsSpread ? 2 : 1;
        if (idleForward && VisibleLeaf + step < currentWindow.FirstLeaf + currentWindow.Leaves.Count ||
            !idleForward && VisibleLeaf != currentWindow.FirstLeaf) return;
        idleTimer.Start();
    }

    private void PrepareIdle()
    {
        idleTimer.Stop();
        if (!Ready || !presentationActive || currentWindow is null || index is null || idleActive) return;
        var target = idleForward ? currentWindow.FirstLeaf + currentWindow.Leaves.Count : currentWindow.FirstLeaf - (IsSpread ? 2 : 1);
        if (target < AdaptivePaginationIndex.OpeningLeafCount || index.Complete && target >= index.Count) return;
        var start = index.Find(target)?.Start ?? index.NextSource;
        if (incoming.Ready && incoming.Document?.Start == start && bankLayouts.TryGetValue(incoming, out var layout) && layout == index.Layout) return;
        sourceCancellation?.Cancel(); sourceCancellation?.Dispose(); sourceCancellation = new();
        idleActive = true;
        PrepareAsync(revision, new(), index, null, target, null, sourceCancellation.Token, true);
    }

    private static ReadingPosition Canonical(ReadingPosition cursor, ContinuousScripture source)
    {
        var verse = source.Spans.FirstOrDefault(s => s.Source.Id == cursor.Ayah)?.Source;
        return verse is not null && cursor.TextOffset == verse.Text.Length && ContinuousScripture.NextAyah(verse.Id) is { } next
            ? new(next) : cursor with { Offset = 0 };
    }

    private void CheckpointChanged(AdaptiveLayoutKey layout) => checkpointVersions[layout] = checkpointVersions.GetValueOrDefault(layout) + 1;

    private async Task PersistCheckpointsAsync(AdaptivePaginationIndex table, CancellationToken token)
    {
        var version = checkpointVersions.GetValueOrDefault(table.Layout);
        if (Checkpoints is not { } store || version <= savedCheckpointVersions.GetValueOrDefault(table.Layout)) return;
        if (await store.SaveAsync(table.Layout, table.Windows.ToArray(), token) && indexes.TryGetValue(table.Layout, out var active) && ReferenceEquals(active, table))
            savedCheckpointVersions[table.Layout] = version;
    }

    private static IReadOnlyList<ReadingLeaf> Boundaries(ContinuousBookView engine, IReadOnlyList<string>? signatures = null) => engine.Leaves.Select((l, i) =>
        new ReadingLeaf(i, Canonical(l.Start, engine.Document!), Canonical(l.End, engine.Document!), l.Spans, signatures?[i] ?? "")).ToArray();

    private async void PrepareAsync(int serial, TaskCompletionSource finished, AdaptivePaginationIndex table,
        ReadingPosition? anchor, int? targetLeaf, bool? forward, CancellationToken cancellation, bool idle = false)
    {
        var prepared = incoming;
        var settings = presentation;
        var width = availableWidth; var height = availableHeight;
        var trace = NavigationTrace.Current;
        if (idle) { NavigationTrace.IdlePreparation.Value = true; trace = 0; NavigationTrace.Mark(0, "IdlePreparationStarted"); }
        try
        {
            if (!checkpointLookups.Contains(table.Layout) && Checkpoints is { } checkpoints)
            {
                var restored = await checkpoints.LoadAsync(table.Layout, cancellation);
                if (serial != revision) return;
                checkpointLookups.Add(table.Layout);
                NavigationTrace.Mark(trace, "PersistentCheckpoint", checkpoints.LastResult);
                if (restored is not null)
                {
                    table = restored; index = restored; indexes[table.Layout] = restored;
                    restoredIndexes.Add(table.Layout);
                }
            }
            while (serial == revision)
            {
                var known = targetLeaf is { } ordinal ? table.Find(ordinal) : table.Find(anchor!);
                var start = known?.Start ?? table.NextSource;
                if (trace != 0) NavigationTrace.Mark(trace, "SourceBoundaryLookup", known is null ? "generate" : "cached");
                var reusable = prepared.Ready && prepared.Document?.Start == start && bankLayouts.TryGetValue(prepared, out var previousLayout) && previousLayout == table.Layout && bankMeasurements.ContainsKey(prepared);
                var source = reusable ? prepared.Document! : requested?.Start == start ? requested : await (ReadSourceAsync?.Invoke(start, cancellation)
                    ?? throw new ResourceException("The canonical scripture source loader is unavailable."));
                if (serial != revision) return;
                if (trace != 0) NavigationTrace.Mark(trace, "NativeBankCacheLookup", reusable ? "prepared-window-hit" : "miss");
                if (!reusable) { bankMeasurements.Remove(prepared); bankLayouts.Remove(prepared); prepared.PrepareForReuse(); }
                prepared.SetPresentationVisible(false);
                Canvas.SetZIndex(prepared, 0); Canvas.SetZIndex(current, 1);
                if (!reusable) prepared.Show(source, settings, latestSelection, latestPalette!, width, height);
                await prepared.WaitForCompositionAsync();
                if (serial != revision) return;
                if (!prepared.Ready) throw new ResourceException(prepared.CompositionError ?? "Incoming scripture did not complete native layout.");
                var measuredLayout = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                if (!DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    if (serial == revision && prepared.IsLoaded && prepared.XamlRoot is not null) prepared.UpdateLayout();
                    measuredLayout.TrySetResult();
                })) throw new ResourceException("The native measurement queue is unavailable.");
                await measuredLayout.Task;
                if (serial != revision) return;
                NavigationTrace.Mark(trace, "NativeCompositionMeasured");
                var measured = reusable ? bankMeasurements[prepared] : Boundaries(prepared);
                if (serial != revision) return;
                bankLayouts[prepared] = table.Layout;
                bankMeasurements[prepared] = measured;
                if (known is not null)
                {
                    try { table.VerifyBoundaries(known, measured); }
                    catch (ResourceException) when (restoredIndexes.Remove(table.Layout))
                    {
                        NavigationTrace.Mark(trace, "PersistentCheckpoint", "native-boundary-mismatch; regenerate from canonical origin");
                        table = new(table.Layout); index = table; indexes[table.Layout] = table;
                        continue;
                    }
                }
                else
                {
                    var final = source.ReachesEnd && prepared.Leaves[^1].End == source.End;
                    var complete = measured.Count - (!final && prepared.Leaves[^1].End == source.End ? 1 : 0);
                    if (!final && table.Layout.Spread) complete = complete / 2 * 2;
                    if (complete <= 0) throw new ResourceException("The native source window cannot hold a complete adaptive spread.");
                    known = table.AppendBoundaries(start, measured.Take(complete).ToArray(), final);
                    CheckpointChanged(table.Layout);
                }
                var contains = targetLeaf is { } target
                    ? target < known.FirstLeaf + known.Leaves.Count
                    : table.Find(anchor!) is not null;
                if (!contains && !table.Complete) continue;
                // Only a displayed window establishes a geometry baseline. A
                // revisited window must match its original baseline exactly.
                if (measured.Any(l => string.IsNullOrEmpty(l.LayoutSignature)))
                    measured = Boundaries(prepared, await prepared.CaptureLayoutSignaturesAsync(cancellation));
                if (serial != revision) return;
                if (idle)
                {
                    bankMeasurements[prepared] = measured;
                    await PersistCheckpointsAsync(table, cancellation);
                    NavigationTrace.Mark(0, "IdlePreparationCompleted");
                    IdleCompletions++;
                    return;
                }
                var establishingGeometry = !known.HasGeometry;
                try { known = table.VerifyOrRecordGeometry(known, measured); }
                catch (ResourceException) when (restoredIndexes.Remove(table.Layout))
                {
                    NavigationTrace.Mark(trace, "PersistentCheckpoint", "native-geometry-mismatch; regenerate from canonical origin");
                    table = new(table.Layout); index = table; indexes[table.Layout] = table;
                    continue;
                }
                restoredIndexes.Remove(table.Layout);
                if (establishingGeometry) CheckpointChanged(table.Layout);
                bankMeasurements[prepared] = measured;
                var selectedLeaf = targetLeaf ?? table.Locate(known, anchor!);
                selectedLeaf = Math.Clamp(selectedLeaf, known.FirstLeaf, known.FirstLeaf + known.Leaves.Count - 1);
                var restore = anchor ?? (viewports[table.Layout].TryGetValue(selectedLeaf, out var saved) ? saved : null);
                prepared.SetIndexedPresentation(known.FirstLeaf, known.Leaves.Count, selectedLeaf - known.FirstLeaf, restore);
                // Complete native layout before swapping. No bitmap or per-frame reflow.
                var fence = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                if (!DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    if (serial == revision && prepared.IsLoaded && prepared.XamlRoot is not null) prepared.UpdateLayout();
                    fence.TrySetResult();
                })) throw new ResourceException("The presentation queue is unavailable.");
                await fence.Task;
                if (serial != revision) return;
                prepared.UpdateAppearance(presentation, latestSelection, latestPalette!);
                var outgoing = current; current = prepared; incoming = outgoing; currentWindow = known;
                Canvas.SetZIndex(current, 1); current.SetPresentationVisible(presentationActive); outgoing.SetPresentationVisible(false);
                NavigationTrace.Incoming(trace);
                preparing = false; pendingLeaf = null;
                if (serial != revision) return;
                if (forward is { } direction) motion.Enter(current, direction ? 18 : -18, AnimationsEnabled && !presentation.ReduceMotion);
                _ = NavigationTrace.AnimationAsync(trace, motion.WaitForCompletionAsync());
                LayoutCommitted?.Invoke(this, EventArgs.Empty);
                await PersistCheckpointsAsync(table, cancellation);
                if (serial == revision) ScheduleIdle();
                return;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (ResourceException ex)
        {
            if (serial != revision) return;
            // Idle preparation must not hide an unexpected composition failure.
            CompositionError = ex.Message; preparing = false;
            if (presentationFailures.Count == 64) presentationFailures.RemoveAt(0);
            presentationFailures.Add(new { Stage = "CanonicalPrepare", Error = ex.Message, Start = prepared.Document?.Start, anchor, targetLeaf });
            LayoutCommitted?.Invoke(this, EventArgs.Empty);
        }
        finally { if (idle && serial == revision) idleActive = false; finished.TrySetResult(); }
    }

    public Task WaitForCompositionAsync() => completion.Task;
    internal Task WaitForPresentationAsync() => motion.WaitForCompletionAsync();
    public ReadingPosition CaptureAnchor() => preparing ? pendingAnchor : current.CaptureAnchor();
    public async Task RestoreAnchorAsync(ReadingPosition anchor)
    {
        var serial = revision;
        await completion.Task;
        if (serial != revision || !Ready) return;
        var known = index?.Find(anchor);
        if (known == currentWindow) await current.RestoreAnchorAsync(anchor);
        else { pendingAnchor = anchor; Begin(anchor, null, null); await completion.Task; }
    }
    public void Turn(bool forward)
    {
        if (idleActive && idleForward != forward) CancelPreparation();
        idleEligible = true;
        idleForward = forward;
        if (!forward && preparing && pendingAnchor == AdaptivePaginationIndex.Origin)
        {
            CancelPreparation();
            WindowRequested?.Invoke(this, new(AdaptivePaginationIndex.Origin, true));
            return;
        }
        if (preparing && (currentWindow is null || !bankLayouts.TryGetValue(current, out var currentLayout) || currentLayout != index?.Layout))
        {
            CancelToVisible();
            return;
        }
        if (index is null || currentWindow is null || !Ready && !preparing) return;
        var target = (pendingLeaf ?? VisibleLeaf) + (forward ? 1 : -1) * (IsSpread ? 2 : 1);
        var trace = NavigationTrace.Current;
        if (trace != 0) NavigationTrace.Mark(trace, "TargetLeafResolved", target.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (target >= index.Count && index.Complete) return;
        if (preparing)
        {
            // A newer pointer/key intent replaces an unfinished page request.
            // The retained current bank is still complete and can be reused.
            sourceCancellation?.Cancel(); ++revision; completion.TrySetResult();
            preparing = false; pendingLeaf = null; pendingAnchor = current.CaptureAnchor();
        }
        if (target < AdaptivePaginationIndex.OpeningLeafCount) { WindowRequested?.Invoke(this, new(AdaptivePaginationIndex.Origin, true)); return; }
        RememberViewport();
        if (target >= currentWindow.FirstLeaf && target < currentWindow.FirstLeaf + currentWindow.Leaves.Count)
        {
            var restore = viewports[index.Layout].TryGetValue(target, out var saved) ? saved : null;
            NavigationTrace.Mark(trace, "NativeBankCacheLookup", "current-window-hit");
            motion.Stop(); current.SetIndexedPresentation(currentWindow.FirstLeaf, currentWindow.Leaves.Count, target - currentWindow.FirstLeaf, restore);
            motion.Enter(current, forward ? 18 : -18, AnimationsEnabled && !presentation.ReduceMotion);
            NavigationTrace.Incoming(trace);
            _ = NavigationTrace.AnimationAsync(trace, motion.WaitForCompletionAsync());
            PositionChanged?.Invoke(this, EventArgs.Empty); LayoutCommitted?.Invoke(this, EventArgs.Empty);
            ScheduleIdle();
        }
        else Begin(null, target, forward);
    }
    public IEnumerable<ContinuousBookView.BookLeaf> VisibleLeaves() => current.VisibleLeaves();
    private void RememberViewport()
    {
        if (Ready && index is not null && currentWindow is not null && viewports.TryGetValue(index.Layout, out var positions))
            positions[VisibleLeaf] = current.CaptureAnchor();
    }
    public bool IsRightSelected(AyahId? id) => current.IsRightSelected(id);
    public void FocusActive() => current.FocusActive();
    internal Verse? HitTestAyah(Point point, UIElement relativeTo) => preparing ? null : current.HitTestAyah(point, relativeTo);
    internal bool LiveBreaksMatchMeasuredLeaves() => Ready && current.LiveBreaksMatchMeasuredLeaves();
    internal bool VisibleSurfacesFitBook() => Ready && current.VisibleSurfacesFitBook();
    public void Clear()
    {
        idleTimer.Stop(); idleActive = false;
        sourceCancellation?.Cancel(); sourceCancellation?.Dispose(); sourceCancellation = null;
        ++revision; completion.TrySetResult(); preparing = false; pendingLeaf = null; motion.Stop(); requested = null; currentWindow = null; bankLayouts.Clear(); bankMeasurements.Clear();
        first.Clear(); second.Clear(); first.SetPresentationVisible(false); second.SetPresentationVisible(false);
    }
    internal object[] TraceStableLayout() => current.TraceStableLayout();
}
