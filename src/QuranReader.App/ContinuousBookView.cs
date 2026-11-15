using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using QuranReader.Core;
using Windows.Foundation;

namespace QuranReader.App;

/// <summary>Native linked-text pagination. Publisher page metadata never constrains the flow.</summary>
internal sealed partial class ContinuousBookView : UserControl
{
    private const int MaxLeaves = 16;
    private readonly Grid book = new() { ColumnSpacing = 24, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly ScrollViewer scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly Grid extent = new();
    private readonly List<BookLeaf> leaves = [];
    private readonly List<BookLeaf> leafPool = [];
    private readonly List<SourceRun> runs = [];
    private readonly List<SurahIntro> headings = [];
    private readonly List<FrameworkElement> flowTargets = [];
    private AyahId? selectedAyah;
    private RichTextBlock? text;
    private ContinuousScripture? requestedSource;
    private ReaderPalette? palette;
    private ReaderSettings settings = new();
    private (double Width, double Height, double Scale) geometry;
    private (double Width, double Height, double Scale) pooledGeometry;
    private bool pooledSpread;
    private bool pooledHasHeaders;
    private bool presentationVisible = true;
    private int revision;
    private int visibleLeaf;
    private TaskCompletionSource completion = new();
    private ReadingPosition? pendingRestore;
    private ReadingPosition? restoredAnchor;
    public ContinuousScripture? Document { get; private set; }
    public bool IsSpread { get; private set; }
    public bool Ready { get; private set; }
    public string? CompositionError { get; private set; }
    public int VisibleLeaf => visibleLeaf;
    public IReadOnlyList<BookLeaf> Leaves => leaves;
    public IReadOnlyList<SourceRun> SourceRuns => runs;
    public int TreeBuilds { get; private set; }
    public event EventHandler<Verse>? AyahSelected;
    public event EventHandler? LayoutCommitted;
    public event EventHandler? PositionChanged;

    public ContinuousBookView()
    {
        book.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        book.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        book.VerticalAlignment = VerticalAlignment.Top;
        extent.Children.Add(book);
        scroll.Content = extent;
        Content = scroll;
        Unloaded += (_, _) => { ++revision; Ready = false; completion.TrySetResult(); };
        scroll.ViewChanged += (_, args) => { if (!args.IsIntermediate && Ready) PositionChanged?.Invoke(this, EventArgs.Empty); };
    }

    public void Show(ContinuousScripture source, ReaderSettings readerSettings, AyahId? selected,
        ReaderPalette readerPalette, double availableWidth, double availableHeight)
    {
        var spread = ReadingLayout.ShowSpread(availableWidth, readerSettings.Scale);
        var width = Math.Max(280, Math.Min(spread ? 1540 : 780, availableWidth));
        // A minimum three-line reading surface remains scrollable at very small heights.
        var height = Math.Max(readerSettings.Scale * ReadingLayout.MushafLineHeightRatio * 3 + 88, availableHeight);
        var nextGeometry = (Math.Round(width, 1), Math.Round(height, 1), readerSettings.Scale);
        var changedSource = !ReferenceEquals(requestedSource, source);
        settings = readerSettings;
        selectedAyah = selected;
        palette = readerPalette;
        if (changedSource || geometry != nextGeometry || IsSpread != spread)
        {
            var anchor = changedSource ? source.Start : CaptureAnchor();
            requestedSource = source;
            geometry = nextGeometry;
            IsSpread = spread;
            pendingRestore = anchor;
            // Rebase at the visible source cursor on recomposition. No pixel estimate
            // is converted into scripture offsets, and selection is independent.
            Document = changedSource ? source : new ContinuousScripture(source.Spans.Select(s => s.Source).ToArray(), anchor, source.End);
            QueueComposition();
        }
        ApplyPaletteAndSelection(selected);
    }

    private void QueueComposition()
    {
        var current = ++revision;
        Ready = false;
        CompositionError = null;
        completion.TrySetResult();
        completion = new();
        var finished = completion;
        var trace = NavigationTrace.Current;
        NavigationTrace.Mark(trace, "NativeCompositionQueued");
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, async () =>
        {
            try
            {
                if (current != revision || Document is null || palette is null) return;
                NavigationTrace.Mark(trace, "NativeTextOverflowCreationStarted");
                BuildDocument();
                NavigationTrace.Mark(trace, "NativeTextOverflowCreationCompleted");
                await NativeLayoutAsync();
                if (current != revision) return;
                while (HasOverflow(leaves[^1]) && leaves.Count <= MaxLeaves)
                {
                    for (var i = 0; i < 8 && leaves.Count <= MaxLeaves; i++) AddLeaf();
                    await NativeLayoutAsync();
                    if (current != revision) return;
                }
                await PreventOrphansAsync(current);
                if (current != revision) return;
                NavigationTrace.Mark(trace, "NativeBoundariesMeasurementStarted");
                MeasureLeaves();
                NavigationTrace.Mark(trace, "NativeBoundariesMeasurementCompleted");
                visibleLeaf = 0;
                Ready = true;
                PresentLeaves();
                if (pendingRestore is { } restored)
                    scroll.ChangeView(null, Math.Max(0, -restored.Offset), null, true);
                LayoutCommitted?.Invoke(this, EventArgs.Empty);
            }
            catch (ResourceException ex)
            {
                CompositionError = ex.Message;
                Ready = false;
                LayoutCommitted?.Invoke(this, EventArgs.Empty);
            }
            finally { finished.TrySetResult(); }
        });
    }

    private Task NativeLayoutAsync()
    {
        var done = new TaskCompletionSource();
        var expected = revision;
        var trace = NavigationTrace.Current;
        NavigationTrace.Mark(trace, "NativeLayoutQueued");
        if (!DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            NavigationTrace.Mark(trace, "WinUiMeasureArrangeStarted");
            if (expected == revision && book.IsLoaded && book.XamlRoot is not null) book.UpdateLayout();
            NavigationTrace.Mark(trace, "WinUiMeasureArrangeCompleted");
            done.TrySetResult();
        })) done.TrySetException(new ResourceException("The native reading layout queue is unavailable."));
        return done.Task;
    }

    public Task WaitForCompositionAsync() => completion.Task;

    internal void UpdateAppearance(ReaderSettings readerSettings, AyahId? selected, ReaderPalette readerPalette)
    {
        settings = readerSettings;
        selectedAyah = selected;
        palette = readerPalette;
        ApplyPaletteAndSelection(selected);
    }

    internal void PrepareForReuse()
    {
        ++revision;
        completion.TrySetResult();
        requestedSource = null;
        // A bank returning from an inactive presentation can retain native break
        // pointers even when its dimensions match. Recreate that chain, preserving
        // the other bank's currently visible scripture until the new one is ready.
        pooledGeometry = default;
        Ready = false;
    }

    internal void SetPresentationVisible(bool visible)
    {
        if (presentationVisible == visible) return;
        presentationVisible = visible;
        // Opacity is a direct visibility switch, never an animation. Keep the
        // spare native overflow chain measured; Collapse invalidates its breaks.
        Opacity = visible ? 1 : 0;
        IsHitTestVisible = visible;
        if (Ready) PresentLeaves();
    }

    private async Task PreventOrphansAsync(int current)
    {
        // Atomic introductions already keep their title and Basmalah together.
        // Only a stranded first scripture line or marker requires a smaller leaf.
        for (var i = 1; i < Math.Min(leaves.Count, MaxLeaves + 1); i++)
        {
            if (current != revision || !HasOverflow(leaves[i - 1])) return;
            var nativeBreak = NativeStart(leaves[i]).Offset;
            var cursor = MapCursor(nativeBreak);
            var slot = runs.FirstOrDefault(r => r.Span.Source.Id == cursor.Ayah);
            if (slot is null) continue;
            var position = cursor.TextOffset;
            while (position < slot.Span.Source.Text.Length && char.IsWhiteSpace(slot.Span.Source.Text[position])) position++;
            var markerOnly = position < slot.Span.Source.Text.Length && slot.Span.Source.Text[position] == '\u06DD';
            var orphan = headings.FirstOrDefault(h => h.Inline.ContentStart.Offset < nativeBreak &&
                runs.First(r => r.Span.Source.Id.Surah == h.Surah).Run.ContentStart.Offset >= nativeBreak);
            if (!markerOnly && orphan is null) continue;
            var previous = leaves[i - 1].Flow;
            var capacity = leaves[i - 1].Capacity;
            var firstLine = slot.Run.ContentStart.GetCharacterRect(LogicalDirection.Forward).Height;
            var introRect = orphan?.Inline.ContentStart.GetCharacterRect(LogicalDirection.Forward) ?? new Rect();
            var cut = orphan is not null ? introRect.Top :
                text!.ContentStart.GetPositionAtOffset(nativeBreak - text.ContentStart.Offset, LogicalDirection.Forward)!.GetCharacterRect(LogicalDirection.Backward).Top;
            var group = orphan is null ? (MeasuredSurahIntro?)null : new(introRect.Top, introRect.Height, firstLine);
            var fits = group is { } measured && SurahTransitionLayout.Fits(capacity, measured);
            if (fits) throw new ResourceException("Native heading grouping disagrees with its measured usable height.");
            if (cut <= 0) throw new ResourceException("The reading surface cannot hold the measured surah introduction and first line.");
            previous.MaxHeight = cut;
            await NativeLayoutAsync();
            if (current != revision) return;
            if (orphan is not null && ContinuousPagination.Compare(MapCursor(NativeStart(leaves[i]).Offset), cursor) != 0)
                throw new ResourceException("Moving a surah introduction unexpectedly moved preceding scripture.");
        }
    }

    private void BuildDocument()
    {
        if (Document is null || palette is null) return;
        TreeBuilds++;
        DisconnectFlows();
        book.Children.Clear();
        leaves.Clear();
        var contentWidth = Math.Max(1, (IsSpread ? (geometry.Width - 24) / 2 : geometry.Width) - 52);
        var headers = Document.Spans.Where(s => s.Source.Id.Ayah == 1 && s.Start == 0).ToDictionary(s => s.Source.Id.Surah,
            s => new SurahHeader(s.Source, settings.Scale, contentWidth, palette));
        var minimumHeight = geometry.Height;
        foreach (var header in headers.Values)
        {
            header.Measure(new(contentWidth, double.PositiveInfinity));
            var verse = Document.Spans.First(s => s.Source.Id.Surah == header.Surah).Source;
            minimumHeight = Math.Max(minimumHeight, header.DesiredSize.Height + header.MeasureFirstLine(verse.Text, settings.Scale) + 88);
        }
        var nativeGeometry = (geometry.Width, minimumHeight, geometry.Scale);
        // WinUI overflow targets retain native break state across font/geometry
        // changes. Reuse only within the same measured geometry; recreate on reflow.
        // Inline UI introductions also retain native overflow state when their
        // visual children are replaced. Recreate those engines at source changes;
        // ordinary scripture windows still reuse the bounded container pool.
        if (pooledGeometry != nativeGeometry || pooledSpread != IsSpread || headers.Count > 0 || pooledHasHeaders)
        {
            leafPool.Clear();
            text = null;
            pooledGeometry = nativeGeometry;
            pooledSpread = IsSpread;
        }
        pooledHasHeaders = headers.Count > 0;
        runs.Clear();
        headings.Clear();
        book.Width = geometry.Width;
        book.Height = minimumHeight;
        extent.Height = minimumHeight;
        restoredAnchor = null;
        book.ColumnSpacing = IsSpread ? 24 : 0;
        book.ColumnDefinitions[0].Width = IsSpread ? new(1, GridUnitType.Star) : new(0);
        // Reuse the bounded native layout engines across source windows rather
        // than churning hundreds of native containers during sustained reading.
        text ??= new();
        text.FontFamily = AyahElements.Hafs;
        text.FontSize = settings.Scale;
        text.LineHeight = settings.Scale * ReadingLayout.MushafLineHeightRatio;
        text.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        text.FlowDirection = FlowDirection.RightToLeft;
        text.Language = "ar-SA";
        text.TextAlignment = TextAlignment.Justify;
        text.TextWrapping = TextWrapping.Wrap;
        text.IsTextSelectionEnabled = true;
        text.Foreground = palette.ScriptureForeground;
        Paragraph? paragraph = null;
        var surah = 0;
        foreach (var span in Document.Spans)
        {
            var verse = span.Source;
            if (surah != verse.Id.Surah)
            {
                surah = verse.Id.Surah;
                if (verse.Id.Ayah == 1 && span.Start == 0)
                {
                    var header = headers[surah];
                    var heading = new Paragraph
                    {
                        TextAlignment = TextAlignment.Center,
                        Margin = new(0),
                        FontSize = 1,
                        LineHeight = header.DesiredSize.Height,
                        LineStackingStrategy = LineStackingStrategy.BlockLineHeight
                    };
                    var inline = new InlineUIContainer { Child = header };
                    headings.Add(new(surah, inline, header));
                    heading.Inlines.Add(inline);
                    text.Blocks.Add(heading);
                }
                paragraph = new Paragraph { Margin = new(0) };
                text.Blocks.Add(paragraph);
            }
            var run = new Run { Text = span.Text };
            var link = new Hyperlink { UnderlineStyle = UnderlineStyle.None, Foreground = palette.ScriptureForeground };
            AutomationProperties.SetName(link, $"Ayah {verse.Id}, canonical page {verse.Page}");
            link.Inlines.Add(run);
            link.Click += OnAyahClick;
            paragraph!.Inlines.Add(link);
            // Presentation separator stays outside all authoritative spans.
            paragraph.Inlines.Add(new Run { Text = " " });
            runs.Add(new(span, run, link));
        }
        for (var i = 0; i < 8; i++) AddLeaf();
        ApplyPaletteAndSelection(selectedAyah);
    }

    private void AddLeaf()
    {
        if (leaves.Count < leafPool.Count)
        {
            var reused = leafPool[leaves.Count];
            reused.Surface.Width = IsSpread ? (geometry.Width - book.ColumnSpacing) / 2 : geometry.Width;
            reused.Surface.Opacity = 0;
            reused.Surface.IsHitTestVisible = false;
            reused.Reference.Text = "";
            if (reused.Flow is RichTextBlock source) source.MaxLines = 0;
            else ((RichTextBlockOverflow)reused.Flow).MaxLines = 0;
            reused.Flow.MaxHeight = double.PositiveInfinity;
            ConnectPrevious(reused.Flow);
            Grid.SetColumn(reused.Surface, 1);
            reused.Flow.InvalidateMeasure();
            reused.Surface.InvalidateMeasure();
            book.Children.Add(reused.Surface);
            leaves.Add(reused);
            flowTargets.Add(reused.Flow);
            return;
        }
        var surface = new Border
        {
            BorderThickness = new(1.5),
            CornerRadius = new(8),
            Padding = new(24, 16, 24, 12),
            Width = IsSpread ? (geometry.Width - book.ColumnSpacing) / 2 : geometry.Width,
            Opacity = 0,
            IsHitTestVisible = false
        };
        var grid = new Grid { RowSpacing = 10 };
        grid.RowDefinitions.Add(new() { Height = new(18) });
        grid.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new() { Height = new(18) });
        var label = new TextBlock { FontSize = 12, Text = $"Adaptive leaf {leaves.Count + 1}", HorizontalAlignment = HorizontalAlignment.Center };
        var reference = new TextBlock { FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center };
        FrameworkElement flow;
        if (leaves.Count == 0) flow = text!;
        else
        {
            var overflow = new RichTextBlockOverflow();
            flow = overflow;
        }
        ConnectPrevious(flow);
        // A measured cutoff must not vertically center the remaining scripture.
        flow.VerticalAlignment = VerticalAlignment.Top;
        grid.Children.Add(label);
        grid.Children.Add(flow);
        grid.Children.Add(reference);
        Grid.SetRow(flow, 1);
        Grid.SetRow(reference, 2);
        surface.Child = grid;
        Grid.SetColumn(surface, 1);
        book.Children.Add(surface);
        leaves.Add(new(surface, flow, label, reference));
        leafPool.Add(leaves[^1]);
        flowTargets.Add(flow);
        NativeTextProviderLifetime.Protect(flow, text);
        NativeTextProviderLifetime.Protect(label);
        NativeTextProviderLifetime.Protect(reference);
        AutomationProperties.SetName(surface, label.Text);
        AutomationProperties.SetAccessibilityView(surface, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
    }

    private void ConnectPrevious(FrameworkElement flow)
    {
        if (leaves.Count == 0) return;
        if (leaves[^1].Flow is RichTextBlock source) source.OverflowContentTarget = (RichTextBlockOverflow)flow;
        else ((RichTextBlockOverflow)leaves[^1].Flow).OverflowContentTarget = (RichTextBlockOverflow)flow;
    }

    private static bool HasOverflow(BookLeaf leaf) => leaf.Flow is RichTextBlock source ? source.HasOverflowContent : ((RichTextBlockOverflow)leaf.Flow).HasOverflowContent;
    private static TextPointer NativeStart(BookLeaf leaf) => leaf.Flow is RichTextBlock source ? source.ContentStart : ((RichTextBlockOverflow)leaf.Flow).ContentStart;

    private ReadingPosition MapCursor(int nativeOffset)
    {
        foreach (var slot in runs)
        {
            var first = slot.Run.ContentStart.Offset;
            var last = slot.Run.ContentEnd.Offset;
            if (last - first != slot.Span.Length) throw new ResourceException("Native text offsets do not match authoritative UTF-16 spans.");
            if (nativeOffset <= first) return new(slot.Span.Source.Id, TextOffset: slot.Span.Start);
            if (nativeOffset < last)
            {
                var offset = slot.Span.Start + nativeOffset - first;
                ContinuousScripture.RequireBoundary(slot.Span.Source.Text, offset);
                return new(slot.Span.Source.Id, TextOffset: offset);
            }
        }
        return Document!.End;
    }

    private void MeasureLeaves()
    {
        if (Document is null) return;
        var used = 1;
        while (used < Math.Min(MaxLeaves, leaves.Count) && HasOverflow(leaves[used - 1])) used++;
        // Keep one measured, hidden overflow target as the continuation sentinel.
        // Its ContentStart is the exact native break after the bounded rendered prefix.
        var keep = used < leaves.Count && HasOverflow(leaves[used - 1]) ? used + 1 : used;
        // Keep the measured native chain intact for its entire lifetime. Removing
        // targets or clearing a published document can invalidate external UIA
        // ranges while they still refer to that document.
        var starts = leaves.Take(used).Select((leaf, i) => i == 0 ? Document.Start : MapCursor(NativeStart(leaf).Offset)).ToArray();
        var end = keep > used ? MapCursor(NativeStart(leaves[used]).Offset) : Document.End;
        var measured = ContinuousPagination.FromMeasuredBreaks(Document, starts, end);
        for (var i = 0; i < used; i++)
        {
            var leaf = leaves[i];
            leaf.Start = measured[i].Start;
            leaf.End = measured[i].End;
            leaf.Spans = measured[i].Spans;
            if (leaf.Spans.Count > 0)
                leaf.Reference.Text = $"Canonical pp. {leaf.Spans[0].Source.Page}–{leaf.Spans[^1].Source.Page} · {leaf.Start.Ayah}";
        }
        leaves.RemoveRange(used, leaves.Count - used);
    }

    private void PresentLeaves()
    {
        for (var i = 0; i < leaves.Count; i++)
        {
            var visible = i == visibleLeaf || (IsSpread && i == visibleLeaf + 1);
            var leaf = leaves[i];
            leaf.Surface.Opacity = visible ? 1 : 0;
            leaf.Surface.IsHitTestVisible = visible;
            // Every linked target must keep the geometry used for measurement.
            // Column 0 is zero-width in single-leaf mode, even for invisible leaves.
            Grid.SetColumn(leaf.Surface, visible && i == visibleLeaf + 1 && IsSpread ? 0 : 1);
            Canvas.SetZIndex(leaf.Surface, visible ? 1 : 0);
            AutomationProperties.SetAccessibilityView(leaf.Surface, visible && presentationVisible ? Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Content : Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        }
        foreach (var slot in runs)
        {
            slot.Link.IsTabStop = presentationVisible && VisibleLeaves().Any(leaf => leaf.Spans.Any(s => s.Source.Id == slot.Span.Source.Id));
            AutomationProperties.SetAccessibilityView(slot.Link, slot.Link.IsTabStop ? Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Content : Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        }
    }

    private void ApplyPaletteAndSelection(AyahId? selected)
    {
        if (palette is null) return;
        foreach (var leaf in leaves)
        {
            leaf.Surface.Background = palette.MushafPageSurface;
            leaf.Surface.BorderBrush = palette.BorderColor;
            leaf.Label.Foreground = leaf.Reference.Foreground = palette.MetadataForeground;
        }
        if (text is not null) text.Foreground = palette.ScriptureForeground;
        foreach (var heading in headings) heading.Header.ApplyPalette(palette);
        foreach (var slot in runs)
        {
            slot.Link.Foreground = palette.ScriptureForeground;
            slot.Link.UnderlineStyle = slot.Span.Source.Id == selected ? UnderlineStyle.Single : UnderlineStyle.None;
        }
    }

    public IEnumerable<BookLeaf> VisibleLeaves() => leaves.Skip(visibleLeaf).Take(IsSpread ? 2 : 1);
    internal void SetIndexedPresentation(int firstLeaf, int count, int visible, ReadingPosition? restore = null)
    {
        if (!Ready || visible < 0 || visible >= count || count > leaves.Count)
            throw new ResourceException("An indexed leaf is outside its native window.");
        for (var i = 0; i < leaves.Count; i++)
        {
            leaves[i].Label.Text = $"Adaptive leaf {firstLeaf + i + 1}";
            AutomationProperties.SetName(leaves[i].Surface, leaves[i].Label.Text);
        }
        visibleLeaf = IsSpread ? visible / 2 * 2 : visible;
        restoredAnchor = null;
        extent.Height = book.Height;
        PresentLeaves(); scroll.ChangeView(null, 0, null, true);
        if (restore is not null) ApplyRestoredAnchor(restore);
    }
    internal bool LiveBreaksMatchMeasuredLeaves() => Ready && leaves.Select((leaf, index) =>
        (index == 0 ? Document!.Start : MapCursor(NativeStart(leaf).Offset)) == leaf.Start).All(match => match);
    internal bool VisibleSurfacesFitBook() => Ready && VisibleLeaves().All(leaf =>
    {
        var bounds = leaf.Surface.TransformToVisual(book).TransformBounds(new Rect(0, 0, leaf.Surface.ActualWidth, leaf.Surface.ActualHeight));
        return bounds.Left >= -0.5 && bounds.Right <= book.ActualWidth + 0.5;
    });
    private Rect CursorRect(ReadingPosition cursor)
    {
        var slot = runs.First(r => r.Span.Source.Id == cursor.Ayah && cursor.TextOffset >= r.Span.Start && cursor.TextOffset < r.Span.End);
        return slot.Run.ContentStart.GetPositionAtOffset(cursor.TextOffset - slot.Span.Start, LogicalDirection.Forward)!.GetCharacterRect(LogicalDirection.Forward);
    }

    private double CursorOrigin(BookLeaf leaf, ReadingPosition cursor) => CursorRect(cursor).Top - CursorRect(leaf.Start).Top;

    public ReadingPosition CaptureAnchor()
    {
        if (!Ready || leaves.Count <= visibleLeaf) return pendingRestore ?? Document?.Start ?? new(new(1, 1));
        if (restoredAnchor is { } restored)
        {
            var owner = VisibleLeaves().FirstOrDefault(l => ContinuousPagination.Compare(l.Start, restored) <= 0 && ContinuousPagination.Compare(restored, l.End) < 0);
            if (owner is not null)
            {
                var origin = CursorOrigin(owner, restored);
                if (origin - scroll.VerticalOffset >= -settings.Scale * ReadingLayout.MushafLineHeightRatio && origin - scroll.VerticalOffset < scroll.ViewportHeight)
                    return restored with { Offset = origin - scroll.VerticalOffset };
            }
        }
        var leaf = leaves[visibleLeaf];
        if (scroll.VerticalOffset < 0.5) return leaf.Start;
        var baseline = CursorRect(leaf.Start).Top;
        foreach (var span in leaf.Spans)
        {
            foreach (var offset in System.Globalization.StringInfo.ParseCombiningCharacters(span.Source.Text))
            {
                if (offset < span.Start || offset >= span.End || offset > 0 && !char.IsWhiteSpace(span.Source.Text[offset - 1])) continue;
                var cursor = new ReadingPosition(span.Source.Id, TextOffset: offset);
                var origin = CursorRect(cursor).Top - baseline;
                if (origin >= scroll.VerticalOffset - 0.5)
                    return cursor with { Offset = origin - scroll.VerticalOffset };
            }
        }
        return leaf.Start with { Offset = -scroll.VerticalOffset };
    }

    public async Task RestoreAnchorAsync(ReadingPosition anchor)
    {
        var serial = revision;
        await WaitForCompositionAsync();
        if (serial != revision || !Ready) return;
        ApplyRestoredAnchor(anchor);
    }

    private void ApplyRestoredAnchor(ReadingPosition anchor)
    {
        if (!Ready) return;
        var found = leaves.FindIndex(leaf => leaf.Spans.Any(span => span.Source.Id == anchor.Ayah && span.Start <= anchor.TextOffset && span.End > anchor.TextOffset));
        if (found >= 0) visibleLeaf = IsSpread ? found / 2 * 2 : found;
        PresentLeaves();
        if (found >= 0)
        {
            restoredAnchor = anchor;
            var offset = Math.Max(0, CursorOrigin(leaves[found], anchor) - anchor.Offset);
            // Extend only the scroll extent, never the measured leaf geometry.
            extent.Height = book.Height + offset;
            if (extent.IsLoaded && extent.XamlRoot is not null)
            {
                extent.UpdateLayout();
                scroll.ChangeView(null, offset, null, true);
            }
        }
    }

    public bool IsRightSelected(AyahId? id) => Ready && leaves.Count > visibleLeaf && leaves[visibleLeaf].Spans.Any(s => s.Source.Id == id);
    public void FocusActive() => (runs.FirstOrDefault(slot => slot.Link.IsTabStop && slot.Span.Source.Id == selectedAyah)
        ?? runs.FirstOrDefault(slot => slot.Link.IsTabStop))?.Link.Focus(FocusState.Programmatic);

    internal Verse? HitTestAyah(Point point, UIElement relativeTo)
    {
        if (!Ready) return null;
        foreach (var leaf in VisibleLeaves())
        {
            var local = relativeTo.TransformToVisual(leaf.Flow).TransformPoint(point);
            if (local.X < 0 || local.Y < 0 || local.X > leaf.Flow.ActualWidth || local.Y > leaf.Flow.ActualHeight) continue;
            var pointer = leaf.Flow is RichTextBlock source ? source.GetPositionFromPoint(local) : ((RichTextBlockOverflow)leaf.Flow).GetPositionFromPoint(local);
            if (pointer is null) continue;
            return runs.FirstOrDefault(r => pointer.Offset >= r.Run.ContentStart.Offset && pointer.Offset < r.Run.ContentEnd.Offset)?.Span.Source;
        }
        return null;
    }

    private void OnAyahClick(Hyperlink sender, HyperlinkClickEventArgs args)
    {
        var slot = runs.FirstOrDefault(run => ReferenceEquals(run.Link, sender));
        if (slot is not null) AyahSelected?.Invoke(this, slot.Span.Source);
    }

    private void DisconnectFlows()
    {
        foreach (var slot in runs) slot.Link.Click -= OnAyahClick;
        // Release ownership of the complete immutable graph. Native automation
        // ranges may outlive its visual attachment; never dismantle their owners.
        // There is no retirement list and no dependency on frame callbacks.
        flowTargets.Clear();
        leafPool.Clear();
        text = null;
    }
    public void Clear()
    {
        ++revision;
        completion.TrySetResult();
        DisconnectFlows();
        book.Children.Clear();
        leaves.Clear(); leafPool.Clear(); runs.Clear(); headings.Clear();
        text = null; requestedSource = null; Document = null; Ready = false;
    }

    internal sealed record SourceRun(ScriptureSpan Span, Run Run, Hyperlink Link);
    internal sealed record SurahIntro(int Surah, InlineUIContainer Inline, SurahHeader Header);
    internal sealed class BookLeaf(Border surface, FrameworkElement flow, TextBlock label, TextBlock reference)
    {
        public Border Surface { get; } = surface;
        public FrameworkElement Flow { get; } = flow;
        public double Capacity => ((Grid)Surface.Child).RowDefinitions[1].ActualHeight;
        public TextBlock Label { get; } = label;
        public TextBlock Reference { get; } = reference;
        public ReadingPosition Start { get; set; } = new(new(1, 1));
        public ReadingPosition End { get; set; } = new(new(1, 1));
        public IReadOnlyList<ScriptureSpan> Spans { get; set; } = [];
    }
}

internal sealed record BookWindowRequest(ReadingPosition Anchor, bool Preceding, bool RestoreEnd = false);
