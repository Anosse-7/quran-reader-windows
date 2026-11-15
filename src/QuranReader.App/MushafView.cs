using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using QuranReader.Core;
using Windows.Foundation;

namespace QuranReader.App;

internal interface IPagePresentation
{
    ReadingPosition CaptureAnchor();
    Task RestoreAnchorAsync(ReadingPosition position);
}

internal sealed class MushafPageView : Grid, IPagePresentation
{
    // Direction D 3-Tier Traditional-Modern Framing:
    // Outer frame (1.5px accent rule) -> 4px margin spacer -> Inner frame (0.75px border on MushafPageSurface)
    private readonly Border outerFrame = new()
    {
        BorderThickness = new Thickness(1.5),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(4)
    };

    private readonly Border innerFrame = new()
    {
        BorderThickness = new Thickness(0.75),
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(24, 16, 24, 16)
    };

    private readonly TranslateTransform pageTransform = new();

    // Top Running Page Header
    private readonly Grid headerGrid = new();
    private readonly TextBlock topSurahHeader = new()
    {
        FontFamily = AyahElements.Hafs,
        FontSize = 14,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        FlowDirection = FlowDirection.RightToLeft,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly TextBlock topJuzHeader = new()
    {
        FontSize = 12,
        FlowDirection = FlowDirection.RightToLeft,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Center,
        Opacity = 0.85
    };
    private readonly Border headerDivider = new()
    {
        Height = 1,
        Margin = new Thickness(0, 8, 0, 10),
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    // Scrollable Continuous Content Area
    private readonly ScrollViewer scroll = new()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        VerticalScrollMode = ScrollMode.Enabled,
        HorizontalScrollMode = ScrollMode.Disabled
    };
    private readonly StackPanel contentPanel = new()
    {
        Orientation = Orientation.Vertical,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };
    private readonly Border anchorTail = new() { IsHitTestVisible = false };

    // Bottom Running Footer & Scroll Affordance
    private readonly StackPanel footerPanel = new()
    {
        Orientation = Orientation.Vertical,
        HorizontalAlignment = HorizontalAlignment.Center,
        Margin = new Thickness(0, 8, 0, 0),
        Spacing = 4
    };
    private readonly Border scrollIndicator = new()
    {
        CornerRadius = new CornerRadius(10),
        BorderThickness = new Thickness(1),
        Padding = new Thickness(12, 3, 12, 4),
        HorizontalAlignment = HorizontalAlignment.Center,
        Opacity = 0.0,
        Visibility = Visibility.Collapsed,
        IsHitTestVisible = false
    };
    private readonly TextBlock scrollIndicatorText = new()
    {
        Text = "تابع القراءة ▾",
        FontSize = 11,
        FontWeight = Microsoft.UI.Text.FontWeights.Medium,
        FlowDirection = FlowDirection.RightToLeft
    };
    private readonly Border pageNumberPill = new()
    {
        CornerRadius = new CornerRadius(12),
        BorderThickness = new Thickness(1),
        Padding = new Thickness(16, 2, 16, 3),
        HorizontalAlignment = HorizontalAlignment.Center
    };
    private readonly TextBlock pageNumber = new()
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        FontSize = 12,
        FontWeight = Microsoft.UI.Text.FontWeights.Medium
    };

    private readonly List<AyahElement> elements = [];
    private IReadOnlyList<Verse>? verses;
    private AyahId? activeSelectedId;
    private ReaderPalette? currentPalette;
    private double currentScale;
    private bool updating;
    private ReadingFitAssessment? lastReportedFit;

    public event EventHandler<Verse>? AyahSelected;
    public event EventHandler? Scrolled;
    public event EventHandler? FitChanged;

    public int Page => verses?.FirstOrDefault()?.Page ?? 0;
    public IReadOnlyList<AyahElement> Elements => elements;
    public ReadingFitAssessment Fit => ReadingFitPolicy.Assess(scroll.ScrollableHeight);
    public bool HasScrollOverflow => Fit.Category == ReadingFitCategory.Scrollable;
    public double CurrentScale => currentScale;
    public bool FitsWithoutScroll => Fit.FitsWithoutScroll;
    public bool FitsWithinTolerance => Fit.FitsWithinTolerance;
    public double ExtentHeight => scroll.ExtentHeight;
    public double ViewportHeight => scroll.ViewportHeight;
    public double ScrollableHeight => scroll.ScrollableHeight;
    public double ContentHeight => contentPanel.ActualHeight > 0 ? contentPanel.ActualHeight : contentPanel.DesiredSize.Height;
    public double AvailableViewportHeight => scroll.ViewportHeight > 0 ? scroll.ViewportHeight : Math.Max(200, ActualHeight - 105);
    public double RemainingBottomSpace => Math.Max(0, AvailableViewportHeight - ContentHeight);
    internal FrameworkElement FullPageSurface => outerFrame;
    internal FrameworkElement ScriptureViewport => scroll;

    private static bool IsVisualAttached(FrameworkElement? element, FrameworkElement? relativeTo)
    {
        if (element is null || relativeTo is null) return false;
        if (!element.IsLoaded || !relativeTo.IsLoaded) return false;
        var xr1 = element.XamlRoot;
        var xr2 = relativeTo.XamlRoot;
        return xr1 is not null && xr2 is not null && ReferenceEquals(xr1, xr2);
    }

    public void Clear()
    {
        ++anchorRevision;
        anchorTail.Height = 0;
        verses = null;
        elements.Clear();
        contentPanel.Children.Clear();
        topSurahHeader.Text = "";
        topJuzHeader.Text = "";
        pageNumber.Text = "";
    }

    internal double MeasureRenderedHeight(double fallbackWidth = 480.0)
    {
        var width = (scroll.ActualWidth > 50) ? scroll.ActualWidth : Math.Max(240.0, fallbackWidth - 56.0);
        contentPanel.Measure(new Windows.Foundation.Size(width, double.PositiveInfinity));
        return contentPanel.DesiredSize.Height;
    }

    internal void UpdateLayoutBalance()
    {
        var availH = scroll.ViewportHeight > 50 ? scroll.ViewportHeight : Math.Max(200.0, ActualHeight - 105);
        var contentH = contentPanel.DesiredSize.Height > 0 ? contentPanel.DesiredSize.Height : contentPanel.ActualHeight;

        if (OpeningSpreadLayoutPolicy.IsOpeningPage(Page))
        {
            var (topPad, bottomPad) = OpeningSpreadLayoutPolicy.CalculateOpeningPadding(availH, contentH);
            contentPanel.Padding = new Thickness(0, topPad, 0, bottomPad);
        }
        else
        {
            var (topPad, bottomPad) = StandardPageLayoutPolicy.CalculateStandardPadding(availH, contentH);
            contentPanel.Padding = new Thickness(0, topPad, 0, bottomPad);
        }
    }

    internal void RebuildComposition(
        IReadOnlyList<Verse> page,
        ReaderSettings settings,
        double newScale,
        AyahId? selected,
        ReaderPalette palette)
    {
        verses = page;
        currentScale = newScale;
        elements.Clear();
        contentPanel.Children.Clear();
        BuildContinuousComposition(page, settings, newScale, selected, palette);
    }

    public MushafPageView()
    {
        Unloaded += (_, _) => ++anchorRevision;
        outerFrame.Opacity = 1.0;
        outerFrame.RenderTransform = pageTransform;
        Children.Add(outerFrame);
        outerFrame.Child = innerFrame;

        var rootGrid = new Grid();
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.Children.Add(topJuzHeader);
        headerGrid.Children.Add(topSurahHeader);
        Grid.SetColumn(topJuzHeader, 0);
        Grid.SetColumn(topSurahHeader, 1);

        var scrollContent = new StackPanel();
        scrollContent.Children.Add(contentPanel);
        scrollContent.Children.Add(anchorTail);
        scroll.Content = scrollContent;

        scrollIndicator.Child = scrollIndicatorText;
        pageNumberPill.Child = pageNumber;
        footerPanel.Children.Add(scrollIndicator);
        footerPanel.Children.Add(pageNumberPill);

        rootGrid.Children.Add(headerGrid);
        rootGrid.Children.Add(headerDivider);
        rootGrid.Children.Add(scroll);
        rootGrid.Children.Add(footerPanel);

        Grid.SetRow(headerGrid, 0);
        Grid.SetRow(headerDivider, 1);
        Grid.SetRow(scroll, 2);
        Grid.SetRow(footerPanel, 3);

        innerFrame.Child = rootGrid;

        // ScrollableHeight settles after Show/SizeChanged. Publish only changed
        // measurements; toolbar updates must not rebuild the scripture tree.
        scroll.LayoutUpdated += (_, _) =>
        {
            var fit = Fit;
            if (fit == lastReportedFit) return;
            lastReportedFit = fit;
            FitChanged?.Invoke(this, EventArgs.Empty);
        };

        scroll.ViewChanged += (_, args) =>
        {
            UpdateScrollIndicator();
            if (!args.IsIntermediate && !updating) Scrolled?.Invoke(this, EventArgs.Empty);
        };

        SizeChanged += (_, _) =>
        {
            UpdateLayoutBalance();
            UpdateScrollIndicator();
        };
    }

    private void UpdateScrollIndicator()
    {
        var hasOverflow = HasScrollOverflow;
        if (!hasOverflow)
        {
            scrollIndicator.Visibility = Visibility.Collapsed;
            scrollIndicator.Opacity = 0.0;
            return;
        }

        scrollIndicator.Visibility = Visibility.Visible;
        var atBottom = scroll.VerticalOffset >= (scroll.ScrollableHeight - 12);
        scrollIndicator.Opacity = atBottom ? 0.0 : 0.85;
    }

    public void Show(
        IReadOnlyList<Verse> page,
        ReaderSettings settings,
        AyahId? selected,
        ReaderPalette palette,
        ReadingPosition? anchor = null,
        double availableHeight = 800,
        double? scaleOverride = null,
        bool isForwardTurn = true,
        bool animate = false,
        bool animationsEnabled = true)
    {
        updating = true;
        try
        {
            if (page.Count == 0)
            {
                Clear();
                return;
            }

            var effectiveScale = scaleOverride ?? ReadingLayout.EffectiveMushafScale(settings, availableHeight);
            var pageChanged = !ReferenceEquals(verses, page);
            var paletteChanged = currentPalette?.Tokens != palette.Tokens;
            var scaleChanged = Math.Abs(currentScale - effectiveScale) > 0.1;
            var needsRebuild = pageChanged || paletteChanged || scaleChanged || elements.Count == 0 || contentPanel.Children.Count == 0;
            currentPalette = palette;
            currentScale = effectiveScale;
            var selectionChanged = activeSelectedId != selected;
            activeSelectedId = selected;

            ApplyThemeStyles(palette);

            if (needsRebuild)
            {
                if (pageChanged)
                {
                    ++anchorRevision;
                    anchorTail.Height = 0;
                }
                verses = page;
                elements.Clear();
                contentPanel.Children.Clear();

                BuildContinuousComposition(page, settings, effectiveScale, selected, palette);

                var first = page.FirstOrDefault();
                if (first is not null)
                {
                    topSurahHeader.Text = $"سُورَةُ {first.SurahName}";
                    topJuzHeader.Text = SurahMetadata.JuzNameArabic(first.Juz);
                }
                else
                {
                    topSurahHeader.Text = "";
                    topJuzHeader.Text = "";
                }

                pageNumber.Text = Page > 0 ? $"— {SurahMetadata.ToEasternArabicDigits(Page)} —" : "";
                AutomationProperties.SetName(this, $"Mushaf page {Page}");

                // Preserve active selection or reading anchor
                if (selected.HasValue && page.Any(v => v.Id == selected.Value))
                {
                    ScrollAyahIntoView(selected.Value);
                }
                else if (anchor is not null && page.Any(v => v.Id == anchor.Ayah))
                {
                    ScrollAyahIntoView(anchor.Ayah);
                }
                else
                {
                    scroll.ChangeView(null, 0, null, true);
                }

                if (animate && pageChanged)
                {
                    AnimateEntrance(isForwardTurn, animationsEnabled, settings.ReduceMotion);
                }
            }
            else
            {
                // In-page selection update: update highlighters and links without visual flicker
                UpdateSelectionHighlights(selected, palette);
                if (selectionChanged && selected.HasValue && page.Any(v => v.Id == selected.Value))
                {
                    ScrollAyahIntoView(selected.Value);
                }
            }

            UpdateScrollIndicator();
        }
        finally
        {
            updating = false;
        }
    }

    private void ApplyThemeStyles(ReaderPalette palette)
    {
        outerFrame.Opacity = 1.0;
        outerFrame.BorderBrush = palette.AccentColor;
        outerFrame.Background = palette.CanvasBackground;

        var isMultiSurah = verses is not null && (verses.Select(v => v.Id.Surah).Distinct().Count() > 1 || Page >= 587);
        innerFrame.Padding = (Page <= 2)
            ? new Thickness(28, 20, 28, 20)
            : isMultiSurah
                ? new Thickness(20, 10, 20, 10)
                : new Thickness(24, 16, 24, 16);

        innerFrame.Background = palette.MushafPageSurface;
        innerFrame.BorderBrush = palette.BorderColor;

        topSurahHeader.Foreground = palette.AccentColor;
        topJuzHeader.Foreground = palette.MetadataForeground;
        headerDivider.Background = palette.SeparatorColor;

        scrollIndicator.Background = palette.ToolbarSurface;
        scrollIndicator.BorderBrush = palette.BorderColor;
        scrollIndicatorText.Foreground = palette.MetadataForeground;

        pageNumberPill.Background = palette.ToolbarSurface;
        pageNumberPill.BorderBrush = palette.BorderColor;
        pageNumber.Foreground = palette.MetadataForeground;
    }

    private void BuildContinuousComposition(
        IReadOnlyList<Verse> page,
        ReaderSettings settings,
        double effectiveScale,
        AyahId? selected,
        ReaderPalette palette)
    {
        if (page.Count == 0) return;

        // Group page verses into contiguous blocks by Surah
        var surahGroups = new List<List<Verse>>();
        List<Verse>? currentGroup = null;
        int? currentSurahNo = null;

        foreach (var v in page)
        {
            if (v.Id.Surah != currentSurahNo)
            {
                currentGroup = [];
                surahGroups.Add(currentGroup);
                currentSurahNo = v.Id.Surah;
            }
            currentGroup!.Add(v);
        }

        var isMultiSurahPage = surahGroups.Count > 1 || Page >= 587;

        // Top alignment ensures predictable scroll extent and prevents negative-coordinate clipping
        contentPanel.VerticalAlignment = VerticalAlignment.Top;

        foreach (var group in surahGroups)
        {
            var firstVerse = group[0];
            var surahNo = firstVerse.Id.Surah;

            // Surah Boundary: if this surah begins on this page (Ayah 1), display the Unwan Header and Basmalah
            if (firstVerse.Id.Ayah == 1)
            {
                var totalAyat = SurahMetadata.TotalAyat(surahNo);
                var unwan = AyahElements.CreateUnwanHeader(surahNo, firstVerse.SurahName, totalAyat, palette, isCompact: isMultiSurahPage);
                contentPanel.Children.Add(unwan);

                // Present Basmalah according to publisher rules:
                // Surah 1 (Al-Fatihah) has Basmalah as numbered Ayah 1; Surah 9 (At-Tawbah) has no Basmalah.
                if (SurahMetadata.HasBasmalahHeader(surahNo))
                {
                    var basmalah = AyahElements.CreateBasmalah(palette, effectiveScale, isCompact: isMultiSurahPage);
                    contentPanel.Children.Add(basmalah);
                }
            }

            // Continuous scripture flow for this surah's verses
            var isOpening = OpeningSpreadLayoutPolicy.IsOpeningPage(Page);
            var isShortSurah = isOpening || (group.Count <= 7 && Page >= 601);
            var lineHeightRatio = isOpening ? OpeningSpreadLayoutPolicy.OpeningLineHeightRatio : ReadingLayout.MushafLineHeightRatio;
            var richText = new RichTextBlock
            {
                FontFamily = AyahElements.Hafs,
                FontSize = effectiveScale,
                LineHeight = effectiveScale * lineHeightRatio,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                Foreground = palette.ScriptureForeground,
                FlowDirection = FlowDirection.RightToLeft,
                Language = "ar-SA",
                TextAlignment = isShortSurah ? TextAlignment.Center : TextAlignment.Justify,
                IsTextSelectionEnabled = false,
                MaxWidth = isOpening ? OpeningSpreadLayoutPolicy.MaxScriptureWidth : double.PositiveInfinity,
                HorizontalAlignment = isOpening ? HorizontalAlignment.Center : HorizontalAlignment.Stretch,
                Margin = isMultiSurahPage ? new Thickness(0, 2, 0, 6) : isOpening ? new Thickness(0, 6, 0, 12) : new Thickness(0, 4, 0, 10)
            };

            var paragraph = new Paragraph();
            var charIndex = 0;

            foreach (var verse in group)
            {
                var isSelected = verse.Id == selected;
                var verseLen = verse.Text.Length;

                var link = new Hyperlink
                {
                    UnderlineStyle = UnderlineStyle.None,
                    Foreground = isSelected ? palette.AccentColor : palette.ScriptureForeground
                };
                AutomationProperties.SetName(link, $"Ayah {verse.Id}");

                var verseRef = verse;
                link.Click += (_, _) => AyahSelected?.Invoke(this, verseRef);

                link.Inlines.Add(new Run
                {
                    Text = verse.Text,
                    Foreground = isSelected ? palette.AccentColor : palette.ScriptureForeground
                });
                paragraph.Inlines.Add(link);
                // Explicit presentation separator, outside the immutable source
                // run and its selectable ayah range. No source string is edited.
                paragraph.Inlines.Add(new Run { Text = " " });

                var element = new AyahElement(verse)
                {
                    Link = link,
                    RichText = richText,
                    StartIndex = charIndex,
                    Length = verseLen
                };
                elements.Add(element);

                charIndex += verseLen + 1;
            }

            richText.Blocks.Add(paragraph);

            // Apply highlighters after Blocks are attached to the RichTextBlock
            foreach (var elem in elements.Where(e => ReferenceEquals(e.RichText, richText)))
            {
                if (elem.Verse.Id == selected)
                {
                    richText.TextHighlighters.Add(new TextHighlighter
                    {
                        Background = palette.SelectedSurface,
                        Foreground = palette.ScriptureForeground, // CRITICAL: NEVER LEAVE NULL TO DEFAULT TO BLACK!
                        Ranges = { new TextRange(elem.StartIndex, elem.Length) }
                    });
                }
            }

            contentPanel.Children.Add(richText);
        }

        UpdateLayoutBalance();
    }

    private void UpdateSelectionHighlights(AyahId? selected, ReaderPalette palette)
    {
        // Clear all text highlighters on all rich text blocks
        var visitedRichTexts = new HashSet<RichTextBlock>();
        foreach (var element in elements)
        {
            if (element.RichText is not null && visitedRichTexts.Add(element.RichText))
            {
                element.RichText.TextHighlighters.Clear();
            }

            if (element.Link is not null)
            {
                var isSelected = element.Verse.Id == selected;
                element.Link.Foreground = isSelected ? palette.AccentColor : palette.ScriptureForeground;
                foreach (var inline in element.Link.Inlines)
                {
                    if (inline is Run run)
                    {
                        run.Foreground = isSelected ? palette.AccentColor : palette.ScriptureForeground;
                    }
                }

                if (isSelected && element.RichText is not null)
                {
                    element.RichText.TextHighlighters.Add(new TextHighlighter
                    {
                        Background = palette.SelectedSurface,
                        Foreground = palette.ScriptureForeground, // CRITICAL: NEVER LEAVE NULL!
                        Ranges = { new TextRange(element.StartIndex, element.Length) }
                    });
                }
            }
        }
    }

    public void ScrollAyahIntoView(AyahId id)
    {
        var element = elements.FirstOrDefault(e => e.Verse.Id == id);
        if (element is null || !TryGetAyahBounds(element, out var top, out _)) return;
        if (top < 20 || top + 40 > scroll.ViewportHeight - 20)
            scroll.ChangeView(null, Math.Max(0, scroll.VerticalOffset + top - 30), null, true);
    }

    private Storyboard? entranceAnimation;

    private void AnimateEntrance(bool isForward, bool animationsEnabled, bool reduceMotion)
    {
        entranceAnimation?.Stop();
        entranceAnimation = null;
        pageTransform.X = 0;
        outerFrame.Opacity = 1.0;
        var duration = ReadingLayout.MotionDuration(animationsEnabled, reduceMotion);
        if (duration <= 0)
        {
            pageTransform.X = 0;
            return;
        }

        var sb = new Storyboard();
        var slide = new DoubleAnimation
        {
            From = isForward ? -10.0 : 10.0,
            To = 0.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(duration)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(slide, pageTransform);
        Storyboard.SetTargetProperty(slide, "X");
        sb.Children.Add(slide);
        entranceAnimation = sb;
        sb.Begin();
    }

    public void FocusActive()
    {
        var target = elements.FirstOrDefault(e => e.Verse.Id == activeSelectedId);
        if (target?.RichText is not null)
        {
            target.RichText.Focus(FocusState.Programmatic);
        }
        else
        {
            scroll.Focus(FocusState.Programmatic);
        }
    }

    public void ScrollToBottom()
    {
        scroll.ChangeView(null, scroll.ScrollableHeight, null, true);
    }

    public void ScrollToTop()
    {
        scroll.ChangeView(null, 0, null, true);
    }

    // Coordinates are measured from the actual Hyperlink text pointers. Plain
    // character counts omit inline boundary positions and cannot identify a
    // visible ayah reliably after Arabic shaping and line wrapping.
    private bool TryGetAyahBounds(AyahElement element, out double top, out double bottom)
    {
        top = bottom = 0;
        if (element.RichText is null || element.Link is null || !IsVisualAttached(element.RichText, scroll)) return false;
        try
        {
            var start = element.Link.ContentStart.GetCharacterRect(LogicalDirection.Forward);
            var end = element.Link.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
            if (start.Height <= 0 || end.Height <= 0) return false;
            var origin = element.RichText.TransformToVisual(scroll).TransformPoint(new Point(0, 0));
            top = origin.Y + start.Y;
            bottom = origin.Y + end.Y + end.Height;
            return double.IsFinite(top) && double.IsFinite(bottom);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // A detached text tree has no usable geometry; retry after layout.
            return false;
        }
    }

    public ReadingPosition CaptureAnchor()
    {
        foreach (var element in elements)
        {
            if (TryGetAyahBounds(element, out var top, out var bottom) && bottom > 0 && top < scroll.ViewportHeight)
                return new ReadingPosition(element.Verse.Id, top);
        }
        return new ReadingPosition(verses?.FirstOrDefault()?.Id ?? new AyahId(1, 1), 0);
    }

    private int anchorRevision;

    public async Task RestoreAnchorAsync(ReadingPosition position)
    {
        var revision = ++anchorRevision;
        await LayoutAsync();
        if (revision != anchorRevision) return;
        var element = elements.FirstOrDefault(e => e.Verse.Id == position.Ayah);
        if (element is null || !TryGetAyahBounds(element, out var top, out _)) return;
        if (position.TextOffset > 0 && element.RichText is not null && element.Link?.Inlines.OfType<Run>().FirstOrDefault() is { } run)
        {
            ContinuousScripture.RequireBoundary(element.Verse.Text, position.TextOffset);
            var pointer = run.ContentStart.GetPositionAtOffset(position.TextOffset, LogicalDirection.Forward);
            if (pointer is not null)
                top = element.RichText.TransformToVisual(scroll).TransformPoint(new()).Y + pointer.GetCharacterRect(LogicalDirection.Forward).Y;
        }
        var target = Math.Max(0, scroll.VerticalOffset + top - position.Offset);
        // Add only the trailing room required by this anchor. It is outside the
        // measured scripture composition and is reset when the page changes.
        var naturalExtent = Math.Max(0, scroll.ExtentHeight - anchorTail.ActualHeight);
        anchorTail.Height = Math.Max(0, target - Math.Max(0, naturalExtent - scroll.ViewportHeight));
        await LayoutAsync();
        if (revision != anchorRevision) return;
        scroll.ChangeView(null, target, null, true);
        await LayoutAsync();
    }

    private Task LayoutAsync()
    {
        var source = new TaskCompletionSource();
        if (DispatcherQueue is not null)
        {
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (IsLoaded && XamlRoot is not null) UpdateLayout();
                source.SetResult();
            });
        }
        else
        {
            source.SetResult();
        }
        return source.Task;
    }
}

internal sealed class MushafView : Grid, IPagePresentation
{
    private sealed record LayoutKey(IReadOnlyList<Verse> Right, IReadOnlyList<Verse> Left,
        double Width, double Height, double Scale, bool Fit, ThemePaletteTokens Palette);
    private LayoutKey? lastLayout;
    public MushafPageView RightPage { get; } = new();
    public MushafPageView LeftPage { get; } = new();
    public bool IsSpread { get; private set; }
    public ReadingFitAssessment Fit => ReadingFitPolicy.Assess(Math.Max(RightPage.ScrollableHeight, IsSpread ? LeftPage.ScrollableHeight : 0));
    public bool FitsWithoutScroll => Fit.FitsWithoutScroll;
    public bool FitsWithinTolerance => Fit.FitsWithinTolerance;
    public double CurrentScale => RightPage.CurrentScale;
    public double RightContentHeight => RightPage.ContentHeight;
    public double LeftContentHeight => LeftPage.ContentHeight;
    public double RightViewportHeight => RightPage.ViewportHeight;
    public double LeftViewportHeight => LeftPage.ViewportHeight;
    public double RightScrollableHeight => RightPage.ScrollableHeight;
    public double LeftScrollableHeight => LeftPage.ScrollableHeight;
    public double RightRemainingBottomSpace => RightPage.RemainingBottomSpace;
    public double LeftRemainingBottomSpace => LeftPage.RemainingBottomSpace;
    private MushafPageView active;
    private AyahId? activeSelectedId;

    public event EventHandler<Verse>? AyahSelected;
    public event EventHandler? Scrolled;
    public event EventHandler? FitChanged;

    public MushafView()
    {
        ColumnSpacing = 24;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Stretch;

        // In WinUI, Column 0 is left, Column 1 is right.
        // In an authentic RTL spread:
        // Earlier publisher page renders strictly on the RIGHT (Column 1)
        // Subsequent publisher page renders strictly on the LEFT (Column 0)
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Children.Add(LeftPage);
        Children.Add(RightPage);
        Grid.SetColumn(LeftPage, 0);
        Grid.SetColumn(RightPage, 1);

        active = RightPage;

        foreach (var page in new[] { RightPage, LeftPage })
        {
            page.FitChanged += (_, _) => FitChanged?.Invoke(this, EventArgs.Empty);
            page.AyahSelected += (_, verse) =>
            {
                active = page;
                AyahSelected?.Invoke(this, verse);
            };
            page.Scrolled += (_, _) =>
            {
                active = page;
                Scrolled?.Invoke(this, EventArgs.Empty);
            };
        }
    }

    public void Show(
        IReadOnlyList<Verse> primary,
        IReadOnlyList<Verse> facing,
        ReaderSettings settings,
        AyahId? selected,
        ReadingPosition anchor,
        double availableWidth,
        ReaderPalette palette,
        double availableHeight = 800,
        bool isForwardTurn = true,
        bool animate = false,
        bool animationsEnabled = true)
    {
        if (primary.Count == 0)
        {
            lastLayout = null;
            RightPage.Clear();
            LeftPage.Clear();
            LeftPage.Visibility = Visibility.Collapsed;
            return;
        }

        activeSelectedId = selected;
        IsSpread = facing.Count > 0 && ReadingLayout.ShowSpread(availableWidth, settings.Scale);
        Width = Math.Max(280, IsSpread ? Math.Min(1540, availableWidth) : Math.Min(780, availableWidth));

        ColumnDefinitions[0].Width = IsSpread ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        LeftPage.Visibility = IsSpread ? Visibility.Visible : Visibility.Collapsed;

        var availableWidthPerPage = IsSpread ? Math.Max(280, (Width - ColumnSpacing) / 2) : Width;

        var right = IsSpread && facing.Count > 0 && primary[0].Page > facing[0].Page ? facing : primary;
        var left = IsSpread ? (ReferenceEquals(right, primary) ? facing : primary) : Array.Empty<Verse>();
        if (!IsSpread && facing.Any(v => v.Id == anchor.Ayah)) right = facing;
        var layoutKey = new LayoutKey(right, left, Width, availableHeight, settings.Scale, settings.FitSpread, palette.Tokens);
        if (layoutKey == lastLayout)
        {
            // Selection/commentary changes retain the scripture tree and fit
            // result. Refit only when content, geometry, typography or theme changes.
            RightPage.Show(right, settings, selected, palette, anchor, availableHeight, RightPage.CurrentScale);
            if (IsSpread) LeftPage.Show(left, settings, selected, palette, anchor, availableHeight, LeftPage.CurrentScale);
            return;
        }

        double effectiveScale;
        if (settings.FitSpread && availableHeight > 400)
        {
            if (IsSpread)
            {
                var (spreadScale, _) = ReadingLayout.CalculateSpreadScale(primary, facing, availableWidthPerPage, availableHeight);
                effectiveScale = spreadScale;
            }
            else
            {
                IReadOnlyList<Verse> singlePageVerses;
                if (primary.Any(v => v.Id == anchor.Ayah))
                    singlePageVerses = primary;
                else if (facing.Count > 0 && facing.Any(v => v.Id == anchor.Ayah))
                    singlePageVerses = facing;
                else
                    singlePageVerses = primary;

                var est = ReadingLayout.EstimatePageLayout(singlePageVerses, availableWidthPerPage, availableHeight);
                effectiveScale = est.Scale;
            }
        }
        else
        {
            effectiveScale = settings.Scale;
        }

        if (IsSpread)
        {
            // Right page gets the earlier page, Left page gets the later page
            var first = (facing.Count > 0 && primary[0].Page > facing[0].Page) ? facing : primary;
            var second = ReferenceEquals(first, primary) ? facing : primary;
            RightPage.Show(first, settings, selected, palette, anchor, availableHeight, effectiveScale, isForwardTurn, animate, animationsEnabled);
            LeftPage.Show(second, settings, selected, palette, anchor, availableHeight, effectiveScale, isForwardTurn, animate, animationsEnabled);

            // Real Layout Verification & Optimization Pass:
            if (settings.FitSpread && availableHeight > 400 && first.Count > 0 && second.Count > 0)
            {
                var textColWidth = availableWidthPerPage;
                var isMulti = first.Select(v => v.Id.Surah).Distinct().Count() > 1 || second.Select(v => v.Id.Surah).Distinct().Count() > 1 || first[0].Page >= 587;
                var fixedChrome = isMulti ? 108.0 : 118.0;
                var actualViewportH = RightPage.ViewportHeight > 50 ? RightPage.ViewportHeight : 0;
                var availableContentH = actualViewportH > 50 ? actualViewportH : Math.Max(200.0, availableHeight - fixedChrome);

                var rightH = RightPage.MeasureRenderedHeight(textColWidth);
                var leftH = LeftPage.MeasureRenderedHeight(textColWidth);
                var maxMeasured = Math.Max(rightH, leftH);
                var maxScale = isMulti ? 24.0 : ReadingLayout.MaxFitScale;
                var targetH = Math.Max(180.0, availableContentH - 2.0);

                var iterations = 0;
                // If content overflows at candidate scale, step down to fit comfortably
                while (maxMeasured > targetH && effectiveScale > ReadingLayout.MinComfortableScale && iterations++ < 8)
                {
                    effectiveScale = Math.Max(ReadingLayout.MinComfortableScale, effectiveScale - 0.5);
                    RightPage.RebuildComposition(first, settings, effectiveScale, selected, palette);
                    LeftPage.RebuildComposition(second, settings, effectiveScale, selected, palette);
                    rightH = RightPage.MeasureRenderedHeight(textColWidth);
                    leftH = LeftPage.MeasureRenderedHeight(textColWidth);
                    maxMeasured = Math.Max(rightH, leftH);
                }

                // If content fits with ample bottom headroom, step up to maximize reading comfort
                while (effectiveScale + 0.5 <= maxScale && iterations++ < 12)
                {
                    var testScale = effectiveScale + 0.5;
                    RightPage.RebuildComposition(first, settings, testScale, selected, palette);
                    LeftPage.RebuildComposition(second, settings, testScale, selected, palette);
                    var testRightH = RightPage.MeasureRenderedHeight(textColWidth);
                    var testLeftH = LeftPage.MeasureRenderedHeight(textColWidth);
                    if (Math.Max(testRightH, testLeftH) <= targetH)
                    {
                        effectiveScale = testScale;
                        rightH = testRightH;
                        leftH = testLeftH;
                        maxMeasured = Math.Max(rightH, leftH);
                    }
                    else
                    {
                        // Revert to largest scale that fits
                        RightPage.RebuildComposition(first, settings, effectiveScale, selected, palette);
                        LeftPage.RebuildComposition(second, settings, effectiveScale, selected, palette);
                        break;
                    }
                }

                RightPage.UpdateLayoutBalance();
                LeftPage.UpdateLayoutBalance();
            }
        }
        else
        {
            // Preserve the visible page independently of the selected commentary ayah.
            IReadOnlyList<Verse> page;
            if (primary.Any(v => v.Id == anchor.Ayah))
            {
                page = primary;
            }
            else if (facing.Count > 0 && facing.Any(v => v.Id == anchor.Ayah))
            {
                page = facing;
            }
            else
            {
                page = primary;
            }

            RightPage.Show(page, settings, selected, palette, anchor, availableHeight, effectiveScale, isForwardTurn, animate, animationsEnabled);

            // Single page real layout verification & optimization pass:
            if (settings.FitSpread && availableHeight > 400 && page.Count > 0)
            {
                var textColWidth = availableWidthPerPage;
                var isMulti = page.Select(v => v.Id.Surah).Distinct().Count() > 1 || (page.Count > 0 && page[0].Page >= 587);
                var fixedChrome = isMulti ? 108.0 : 118.0;
                var actualViewportH = RightPage.ViewportHeight > 50 ? RightPage.ViewportHeight : 0;
                var availableContentH = actualViewportH > 50 ? actualViewportH : Math.Max(200.0, availableHeight - fixedChrome);
                var maxScale = isMulti ? 24.0 : ReadingLayout.MaxFitScale;
                var targetH = Math.Max(180.0, availableContentH - 2.0);

                var contentH = RightPage.MeasureRenderedHeight(textColWidth);
                var iterations = 0;

                while (contentH > targetH && effectiveScale > ReadingLayout.MinComfortableScale && iterations++ < 8)
                {
                    effectiveScale = Math.Max(ReadingLayout.MinComfortableScale, effectiveScale - 0.5);
                    RightPage.RebuildComposition(page, settings, effectiveScale, selected, palette);
                    contentH = RightPage.MeasureRenderedHeight(textColWidth);
                }

                while (effectiveScale + 0.5 <= maxScale && iterations++ < 12)
                {
                    var testScale = effectiveScale + 0.5;
                    RightPage.RebuildComposition(page, settings, testScale, selected, palette);
                    var testH = RightPage.MeasureRenderedHeight(textColWidth);
                    if (testH <= targetH)
                    {
                        effectiveScale = testScale;
                        contentH = testH;
                    }
                    else
                    {
                        RightPage.RebuildComposition(page, settings, effectiveScale, selected, palette);
                        break;
                    }
                }

                RightPage.UpdateLayoutBalance();
            }
        }

        active = IsSpread && LeftPage.Elements.Any(e => e.Verse.Id == anchor.Ayah) ? LeftPage : RightPage;
        lastLayout = layoutKey;
    }

    public void FocusActive()
    {
        if (activeSelectedId.HasValue)
        {
            if (RightPage.Elements.Any(e => e.Verse.Id == activeSelectedId.Value))
            {
                RightPage.FocusActive();
                return;
            }
            if (LeftPage.Elements.Any(e => e.Verse.Id == activeSelectedId.Value))
            {
                LeftPage.FocusActive();
                return;
            }
        }
        active.FocusActive();
    }
    public void ScrollActiveToBottom() => active.ScrollToBottom();
    public void ScrollActiveToTop() => active.ScrollToTop();

    public ReadingPosition CaptureAnchor() => active.CaptureAnchor();

    public Task RestoreAnchorAsync(ReadingPosition position)
    {
        active = IsSpread && LeftPage.Elements.Any(e => e.Verse.Id == position.Ayah) ? LeftPage : RightPage;
        return active.RestoreAnchorAsync(position);
    }

    public bool IsRightPageSelected(AyahId? id) => id.HasValue && RightPage.Elements.Any(e => e.Verse.Id == id.Value);
}
