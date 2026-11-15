using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using QuranReader.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.UI.ViewManagement;

namespace QuranReader.App;

public sealed partial class MainWindow
{
    private readonly PointerPageTurnGate pointerTurns = new();
    private ButtonBase? pointerTurnSource;
    private long contextRevision;
    private MenuFlyout? ayahContext;
    private readonly AccessibilitySettings accessibilitySettings = new();
    private (ReadingTheme Theme, ElementTheme Actual, bool HighContrast)? captionTheme;

    private void InitializeDesktopCommands()
    {
        Root.SizeChanged += (_, _) => UpdateDesktopCommands();
        // Native text controls handle ContextRequested for their selection menu.
        // Observe the handled routed event too, then offer whole-ayah actions.
        Root.AddHandler(UIElement.ContextRequestedEvent, new TypedEventHandler<UIElement, ContextRequestedEventArgs>(AyahContextRequested), true);
        void Press(object sender, PointerRoutedEventArgs args)
        {
            pointerTurnSource = sender as ButtonBase;
            NavigationTrace.Input(ReferenceEquals(sender, NextButton) ? "pointer-next" : "pointer-previous");
        }
        NextButton.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(Press), true);
        PreviousButton.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(Press), true);
    }

    private bool AcceptPageActivation(object sender, bool forward)
    {
        ayahContext?.Hide();
        if (!ReferenceEquals(sender, pointerTurnSource)) return true;
        pointerTurnSource = null;
        return pointerTurns.TryAccept(forward, Environment.TickCount64);
    }

    private void UpdateDesktopCommands()
    {
        if (Root.ActualWidth <= 0) return;
        var layout = DesktopCommandLayout.ForWidth(Root.ActualWidth);
        ReaderBrand.Visibility = layout.ShowBrand ? Visibility.Visible : Visibility.Collapsed;
        BrandColumn.Width = layout.ShowBrand ? new(1, GridUnitType.Star) : new(0);
        ReadingStatus.Visibility = layout.ShowStatus ? Visibility.Visible : Visibility.Collapsed;
        ModePicker.Width = layout.Compact ? 232 : 250;
        var scaleText = $"{(model.Settings.Mode == ReadingMode.Mushaf && mushafView.CurrentScale > 0 ? mushafView.CurrentScale : model.Settings.Scale):0}";
        if (ScaleLabel.Text != scaleText)
        {
            ScaleLabel.Text = scaleText;
            AutomationProperties.SetName(ScaleLabel, $"Current reading font size, {scaleText} DIP");
        }
        if (ScaleLabel.Foreground is not SolidColorBrush ink || ink.Color != activePalette.MetadataForeground.Color)
            ScaleLabel.Foreground = activePalette.MetadataForeground;
        ToolTipService.SetToolTip(ScaleLabel, "Reading font size in DIP; independent of window width");
        // In Fit Spread, either size button also enters manual reading. Keep
        // that existing action available even at a saved manual-scale bound.
        var fitted = model.Settings.Mode == ReadingMode.Mushaf && model.Settings.FitSpread;
        SmallerButton.IsEnabled = model.Ready && (fitted || model.Settings.Scale > ReadingLayout.MinComfortableScale);
        LargerButton.IsEnabled = model.Ready && (fitted || model.Settings.Scale < ReadingLayout.MaxManualScale);
        var spread = IsContinuousPreview ? (IsPreviewOpening ? mushafView.IsSpread : continuousBook.IsSpread) : model.Settings.Mode == ReadingMode.Mushaf && mushafView.IsSpread;
        var unit = IsContinuousPreview ? (spread ? "Leaves" : "Leaf") : (spread ? "Spread" : "Page");
        SetTextContent(NextButton, layout.Compact ? "Next" : $"Next {unit}");
        SetTextContent(PreviousButton, layout.Compact ? "Previous" : $"Previous {unit}");
        SetCommandContent(AppearanceButton, layout.Compact, Symbol.Setting, "Appearance");
        SetCommandContent(BookmarksButton, layout.Compact, Symbol.Bookmarks, "Bookmarks");
        SetCommandContent(CopyAyahButton, layout.Compact, Symbol.Copy, "Copy ayah");
        var saved = CurrentActionVerse is not null && model.IsBookmarked;
        SetTextContent(BookmarkButton, layout.Compact ? (saved ? "★" : "☆") : (saved ? "★ Saved" : "☆ Bookmark"));
        var bookmarkName = $"{(saved ? "Remove bookmark for" : "Bookmark")} selected ayah {CurrentActionVerse?.Id}";
        if (AutomationProperties.GetName(BookmarkButton) != bookmarkName) AutomationProperties.SetName(BookmarkButton, bookmarkName);
        CopyAyahButton.IsEnabled = BookmarkButton.IsEnabled = model.Ready && CurrentActionVerse is not null;
        TafsirToggle.IsEnabled = model.Ready;
        var animate = !model.Settings.ReduceMotion && uiSettings.AnimationsEnabled;
        AppearanceButton.Flyout.AreOpenCloseAnimationsEnabled = MoreButton.Flyout.AreOpenCloseAnimationsEnabled = animate;
    }

    private static void SetTextContent(ContentControl control, string value)
    {
        if (!Equals(control.Content, value)) control.Content = value;
    }

    private static void SetCommandContent(ContentControl control, bool compact, Symbol icon, string label)
    {
        if (compact)
        {
            if (control.Content is not SymbolIcon symbol || symbol.Symbol != icon) control.Content = new SymbolIcon(icon);
        }
        else if (!Equals(control.Content, label)) control.Content = label;
    }

    private void ApplyCaptionTheme()
    {
        if (!AppWindowTitleBar.IsCustomizationSupported()) return;
        var identity = (model.Settings.Theme, Root.ActualTheme, accessibilitySettings.HighContrast);
        if (captionTheme == identity) return;
        captionTheme = identity;
        var system = model.Settings.Theme == ReadingTheme.System || accessibilitySettings.HighContrast;
        var bar = AppWindow.TitleBar;
        bar.BackgroundColor = bar.ButtonBackgroundColor = bar.InactiveBackgroundColor = bar.ButtonInactiveBackgroundColor = system ? null : activePalette.CanvasBackground.Color;
        bar.ForegroundColor = bar.ButtonForegroundColor = system ? null : activePalette.ScriptureForeground.Color;
        bar.InactiveForegroundColor = bar.ButtonInactiveForegroundColor = system ? null : activePalette.MetadataForeground.Color;
        bar.ButtonHoverBackgroundColor = system ? null : activePalette.HoverSurface.Color;
        bar.ButtonPressedBackgroundColor = system ? null : activePalette.SelectedSurface.Color;
        bar.ButtonHoverForegroundColor = bar.ButtonPressedForegroundColor = system ? null : activePalette.ScriptureForeground.Color;
    }

    private void FullscreenFromMenu(object sender, RoutedEventArgs args)
    {
        var anchor = CaptureAnchor();
        AppWindow.SetPresenter(AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen ? AppWindowPresenterKind.Overlapped : AppWindowPresenterKind.FullScreen);
        RestoreAnchor(anchor);
    }

    private static Rect ElementBounds(FrameworkElement element, UIElement root) => element.TransformToVisual(root).TransformBounds(new(0, 0, element.ActualWidth, element.ActualHeight));

    private Verse? ContextVerseAt(Point point)
    {
        if (TafsirPopupOverlay.Visibility == Visibility.Visible ||
            (TafsirBottomSheet.Visibility == Visibility.Visible && ElementBounds(TafsirBottomSheet, Root).Contains(point)) ||
            (TafsirPanel.Visibility == Visibility.Visible && ElementBounds(TafsirPanel, Root).Contains(point))) return null;
        if (!ElementBounds(ReadingArea, Root).Contains(point)) return null;
        if (IsContinuousPreview && !IsPreviewOpening) return continuousBook.HitTestAyah(point, Root);
        if (!IsBookMode) return cards.FirstOrDefault(c => c.Card.IsLoaded && ElementBounds(c.Card, Root).Contains(point))?.Verse;
        var elements = mushafView.RightPage.Elements.Concat(mushafView.LeftPage.Elements).ToArray();
        foreach (var rich in elements.Select(e => e.RichText).OfType<RichTextBlock>().Distinct())
        {
            if (!rich.IsLoaded || !ElementBounds(rich, Root).Contains(point)) continue;
            var pointer = rich.GetPositionFromPoint(Root.TransformToVisual(rich).TransformPoint(point));
            if (pointer is not null) return elements.FirstOrDefault(e => ReferenceEquals(e.RichText, rich) && e.Link is { } link && pointer.Offset >= link.ContentStart.Offset && pointer.Offset < link.ContentEnd.Offset)?.Verse;
        }
        return null;
    }

    private async void AyahContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (!model.Ready) return;
        Verse? verse;
        var pointerRequest = args.TryGetPosition(Root, out var point);
        if (pointerRequest) verse = ContextVerseAt(point);
        else
        {
            var focused = FocusManager.GetFocusedElement(Root.XamlRoot);
            verse = focused is Hyperlink link
                ? continuousBook.SourceRuns.FirstOrDefault(r => ReferenceEquals(r.Link, link))?.Span.Source
                    ?? mushafView.RightPage.Elements.Concat(mushafView.LeftPage.Elements).FirstOrDefault(e => ReferenceEquals(e.Link, link))?.Verse
                : null;
            if (verse is null && !IsBookMode && focused is DependencyObject node)
                for (var current = node; current is not null; current = VisualTreeHelper.GetParent(current))
                    if (current is FrameworkElement { Tag: Verse tagged }) { verse = tagged; break; }
        }
        if (verse is null) return;
        args.Handled = true;
        var generation = ++contextRevision;
        ayahContext?.Hide();
        var anchor = CaptureAnchor();
        await model.SelectAsync(verse, false);
        if (generation != contextRevision || model.SelectedVerse?.Id != verse.Id || closing) return;
        RestoreAnchor(anchor);
        var menu = new MenuFlyout { AreOpenCloseAnimationsEnabled = !model.Settings.ReduceMotion && uiSettings.AnimationsEnabled };
        menu.Items.Add(new MenuFlyoutItem { Text = $"Ayah {verse.Id} · canonical page {verse.Page}", IsEnabled = false, MinHeight = 40 });
        menu.Items.Add(new MenuFlyoutSeparator());
        var tafsir = new MenuFlyoutItem { Text = "Open Tafsir", Icon = new SymbolIcon(Symbol.Comment), MinHeight = 40 };
        tafsir.Click += async (_, _) => await model.SelectAsync(verse);
        var bookmark = new MenuFlyoutItem { Text = model.Bookmarks.Contains(verse.Id) ? "Remove bookmark" : "Bookmark ayah", Icon = new SymbolIcon(Symbol.Bookmarks), MinHeight = 40 };
        bookmark.Click += async (_, _) =>
        {
            await model.SelectAsync(verse, false);
            if (model.SelectedVerse?.Id == verse.Id) await model.ToggleBookmarkAsync();
        };
        var copy = new MenuFlyoutItem { Text = "Copy complete ayah", Icon = new SymbolIcon(Symbol.Copy), MinHeight = 40 };
        copy.Click += (_, _) => CopyWholeAyah(verse);
        menu.Items.Add(tafsir); menu.Items.Add(bookmark); menu.Items.Add(copy);
        ayahContext = menu;
        menu.Closed += (_, _) => { if (ReferenceEquals(ayahContext, menu)) ayahContext = null; };
        if (pointerRequest) menu.ShowAt(Root, point);
        else menu.ShowAt(ReadingArea);
    }

    private static void CopyWholeAyah(Verse verse)
    {
        var package = new DataPackage();
        package.SetText(verse.Text);
        Clipboard.SetContent(package);
    }
}
