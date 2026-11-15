using System.ComponentModel;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using QuranReader.Core;
using QuranReader.Data;
using Windows.Foundation;
using Windows.System;
using Windows.UI.ViewManagement;

namespace QuranReader.App;

public sealed partial class MainWindow : Window
{
    private readonly ContentRepository content;
    private readonly ReaderViewModel model;
    private readonly string userStatePath;
    private readonly PublisherFont fontResource = new();
    private readonly DispatcherTimer positionTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly List<AyahElement> cards = [];
    private readonly Border readingTail = new() { IsHitTestVisible = false };
    private readonly MushafView mushafView = new();
    private readonly RetainedContinuousBookView continuousBook = new() { Visibility = Visibility.Collapsed };
    private bool IsBookMode => model.Settings.Mode != ReadingMode.Tafsir;
    private bool IsContinuousPreview => model.Settings.Mode == ReadingMode.AdaptiveBook;
    private bool IsPreviewOpening => IsContinuousPreview && (model.Position.Ayah.Surah == 1 || (model.Position.Ayah.Surah == 2 && model.Position.Ayah.Ayah <= 5));
    private Task adaptiveNavigation = Task.CompletedTask;
    private int pendingSettingsChanges;
    private long settingsRequestRevision;
    private IReadOnlyList<Verse>? renderedBookSource;
    private readonly UISettings uiSettings = new();
    private Storyboard? activeAnimation;
    private ScrollViewer? scroller;
    private IReadOnlyList<Verse>? rendered;
    private bool updating;
    private bool layoutChanging;
    private bool closing;
    private int layoutRevision;
    private ReadingPosition? visibleAnchor;
    private ReaderPalette activePalette = ReaderPalette.For(ReadingTheme.Sepia);
    private bool lastNavigationForward = true;
    private bool pageTurnAnimated;
    private (AyahId? Ayah, string Text, ThemePaletteTokens Tokens)? supplementalScriptureKey;
    private (AyahId? Ayah, string Text, ThemePaletteTokens Tokens)? supplementalTafsirKey;
    private (AyahId? Ayah, string Text, ThemePaletteTokens Tokens)? sidebarTafsirKey;
    internal ElementTheme? SimulatedSystemTheme { get; set; }

    public MainWindow()
    {
        InitializeComponent();
        NavigationTrace.Initialize();
        content = new(Path.Combine(AppContext.BaseDirectory, "Content", "quran_reader_candidate.sqlite3"));
        var userPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuranReader", "user.sqlite3");
        userStatePath = userPath;
        model = new(NavigationTrace.Enabled ? new TracedContentRepository(content) : content,
            NavigationTrace.Enabled ? new TracedUserStateStore(new UserStateStore(userStatePath)) : new UserStateStore(userStatePath));
        continuousBook.ReadSourceAsync = async (anchor, token) =>
        {
            var trace = NavigationTrace.Current;
            NavigationTrace.Mark(trace, "ScriptureDataPreparationStarted");
            var source = await model.ReadContinuousSourceAsync(anchor, token);
            NavigationTrace.Mark(trace, "ScriptureDataPreparationCompleted");
            return source;
        };
        model.PropertyChanged += ModelChanged;
        InitializeDesktopCommands();
        ReadingArea.SizeChanged += (_, args) => { if (!updating && model.Ready) ReadingSizeChanged(ReadingGrid, args); };

        VerseList.Footer = readingTail;
        ReadingArea.Children.Add(mushafView);
        ReadingArea.Children.Add(continuousBook);
        AyahElements.RefineList(VerseList);

        mushafView.AyahSelected += async (_, verse) => await model.SelectAsync(verse);
        continuousBook.AyahSelected += async (_, verse) => await model.SelectAsync(verse);
        continuousBook.PreparationChanged += (_, _) => { if (!closing && IsContinuousPreview) UpdateReadingChrome(); };
        continuousBook.LayoutCommitted += (_, _) =>
        {
            if (closing || !IsContinuousPreview || IsPreviewOpening || model.IsBusy) return;
            if (continuousBook.CompositionError is { } error)
            {
                ErrorBar.Message = error;
                ErrorBar.IsOpen = true;
            }
            else if (continuousBook.Ready)
            {
                PresentAdaptiveSurface();
                if (continuousBook.Document is { } presented) model.BindPresentedContinuous(presented);
                visibleAnchor = continuousBook.CaptureAnchor();
                positionTimer.Stop();
                positionTimer.Start();
            }
            UpdateReadingChrome();
        };
        continuousBook.PositionChanged += (_, _) =>
        {
            if (closing || !IsContinuousPreview || IsPreviewOpening || model.IsBusy) return;
            visibleAnchor = continuousBook.CaptureAnchor();
            positionTimer.Stop(); positionTimer.Start();
        };
        continuousBook.WindowRequested += async (_, request) =>
        {
            if (request.Preceding && request.Anchor.Ayah == new AyahId(2, 6) && request.Anchor.TextOffset == 0)
                await (adaptiveNavigation = model.GoToAsync(continuousBook.IsSpread ? new(1, 1) : new(2, 1), activateSelection: false));
            else await (adaptiveNavigation = model.LoadContinuousWindowAsync(request.Anchor));
        };
        mushafView.FitChanged += (_, _) =>
        {
            if (!closing && model.Ready && model.Settings.Mode == ReadingMode.Mushaf)
                UpdateReadingChrome();
        };
        mushafView.Scrolled += (_, _) =>
        {
            if (layoutChanging) return;
            visibleAnchor = CaptureAnchor();
            positionTimer.Stop();
            positionTimer.Start();
        };

        AppWindow.Resize(new Windows.Graphics.SizeInt32(1180, 900));
        Root.PreviewKeyDown += OnRootPreviewKeyDown;
        Root.Unloaded += (_, _) => { closing = true; ++layoutRevision; };
        positionTimer.Tick += SaveVisiblePosition;
        AppWindow.Closing += WindowClosing;
        Root.ActualThemeChanged += (_, _) =>
        {
            if (model.Settings.Theme == ReadingTheme.System)
            {
                ApplyPalette();
                Render();
            }
        };
    }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        Render();
        try
        {
            await fontResource.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Assets", "kfgqpc_hafs_v30.ttf"));
        }
        catch (ResourceException ex)
        {
            ErrorBar.IsOpen = true;
            ErrorBar.Message = ex.Message;
            return;
        }

        var renderingCompatibility = await Task.Run(() => string.Join(";", "checkpoints-v1;signatures-v1;WinAppSDK-2.5.1",
            Environment.OSVersion.Version, System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture,
            System.Diagnostics.FileVersionInfo.GetVersionInfo(Path.Combine(Environment.SystemDirectory, "dwrite.dll")).FileVersion));
        continuousBook.Checkpoints = new(Path.Combine(Path.GetDirectoryName(userStatePath)!, "pagination-checkpoints"),
            renderingCompatibility, content.ValidateCheckpointPositionsAsync);
        await model.InitializeAsync();
        Render();
        NativeTextProviderLifetime.ProtectTree(Root);
        scroller = FindVisual<ScrollViewer>(VerseList);
        if (scroller is not null) scroller.ViewChanged += ViewChanged;
        Root.XamlRoot.Changed += XamlRootChanged;
    }

    private void ModelChanged(object? sender, PropertyChangedEventArgs args) => Render();

    private static bool IsVisualAttached(FrameworkElement? element, FrameworkElement? relativeTo)
    {
        if (element is null || relativeTo is null) return false;
        if (!element.IsLoaded || !relativeTo.IsLoaded) return false;
        var xr1 = element.XamlRoot;
        var xr2 = relativeTo.XamlRoot;
        return xr1 is not null && xr2 is not null && ReferenceEquals(xr1, xr2);
    }

    private void Render()
    {
        if (closing) return;
        updating = true;
        try
        {
            ErrorBar.IsOpen = model.Error is not null;
            ErrorBar.Message = model.Error ?? "";
            SurahPicker.IsEnabled = PagePicker.IsEnabled = model.Ready;

            var isLoading = model.IsBusy;
            LoadingRing.IsActive = isLoading;
            LoadingRing.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;

            var isTrulyEmpty = model.Ready && !model.IsBusy && model.Verses.Count == 0;
            EmptyNotice.Visibility = isTrulyEmpty ? Visibility.Visible : Visibility.Collapsed;
            if (isTrulyEmpty) EmptyNotice.Text = model.Error ?? "لا توجد آيات لعرضها";

            var isMushaf = IsBookMode;
            var isSidebarPresentation = !isMushaf || model.Settings.Presentation is TafsirPresentation.SlidingSidebar or TafsirPresentation.EdgeDrawer;
            var drawerWidth = Math.Clamp(model.Settings.DrawerWidth, 300, 560);
            var effectiveScale = mushafView.CurrentScale > 0 ? mushafView.CurrentScale : model.Settings.Scale;
            var spreadThreshold = ReadingLayout.SpreadThreshold(effectiveScale);
            var singlePageThreshold = ReadingLayout.SinglePageThreshold(effectiveScale);
            var totalGridWidth = ReadingGrid.ActualWidth > 0 ? ReadingGrid.ActualWidth - (IsBookMode ? EdgeRailWidth : 0) : 1200;

            var canDockSpread = totalGridWidth >= (spreadThreshold + drawerWidth + 30);
            var wasSpread = totalGridWidth >= spreadThreshold;
            var canDockSingle = !wasSpread && (totalGridWidth >= singlePageThreshold + drawerWidth + 24);
            var canDock = (!IsContinuousPreview || IsEdgeDrawer) && isSidebarPresentation && model.Settings.TafsirOpen && (canDockSpread || canDockSingle);
            ReadingGrid.ColumnSpacing = 0;

            var availableReadingWidth = totalGridWidth;
            if (canDock) availableReadingWidth = Math.Max(300, availableReadingWidth - drawerWidth - 20);

            BookmarkButton.IsEnabled = TafsirToggle.IsEnabled = model.Ready;
            if (!ReferenceEquals(SurahPicker.ItemsSource, model.Surahs)) SurahPicker.ItemsSource = model.Surahs;
            SurahPicker.SelectedItem = model.Surahs.FirstOrDefault(s => s.Number == model.ActiveSurah?.Number);
            PagePicker.Text = model.ActivePage.ToString(System.Globalization.CultureInfo.InvariantCulture);
            ThemePicker.SelectedIndex = (int)model.Settings.Theme;
            ModePicker.SelectedIndex = (int)model.Settings.Mode;
            ToolTipService.SetToolTip(ModePicker, IsContinuousPreview ? "Adaptive Continuous Book — Preview · dynamic reading leaves, canonical page references retained" : "Reading mode");
            ReferenceButton.Visibility = IsContinuousPreview ? Visibility.Visible : Visibility.Collapsed;
            CopyAyahButton.Visibility = Visibility.Visible;
            SubtitleLabel.Text = IsContinuousPreview ? "Adaptive Continuous Book — Preview" : "Hafs · A quiet place to read";
            if (!IsContinuousPreview)
            {
                PositionLabel.FontSize = 14;
                PositionLabel.TextTrimming = TextTrimming.None;
                ProvisionalNotice.Text = "المصحف الشريف برواية حفص عن عاصم · التفسير الميسر";
            }
            TafsirPresentationPicker.SelectedIndex = (int)model.Settings.Presentation;
            TafsirPresentationPicker.Visibility = isMushaf ? Visibility.Visible : Visibility.Collapsed;
            TafsirPresentationLabel.Visibility = TafsirPresentationPicker.Visibility;
            TafsirToggle.IsChecked = model.Settings.TafsirOpen;
            PinButton.IsChecked = model.Settings.DrawerPinned;
            FitToggle.Visibility = isMushaf && !IsContinuousPreview ? Visibility.Visible : Visibility.Collapsed;

            TafsirTitle.Text = model.TafsirTitle;
            TafsirAttribution.Text = "التفسير الميسر · مجمع الملك فهد لطباعة المصحف الشريف";

            ApplyPalette();

            if (isLoading)
            {
                ++surfaceRevision;
                continuousBook.CancelPreparation();
                // Keep any existing native scripture mounted while the incoming source loads.
                // The loading indicator and disabled navigation describe the pending operation.
            }
            else if (IsContinuousPreview && !IsPreviewOpening)
            {
                ++surfaceRevision;
                continuousBook.Visibility = Visibility.Visible;
                if (!adaptivePresented) continuousBook.SetPresentationActive(false);
                if (model.Continuous is { } source)
                    continuousBook.Show(source, model.Settings, CurrentActionVerse?.Id, activePalette,
                        availableReadingWidth, ReadingGrid.ActualHeight > 0 ? ReadingGrid.ActualHeight : 800);
            }
            else if (IsBookMode)
            {
                continuousBook.CancelPreparation();
                if (!ReferenceEquals(renderedBookSource, model.Verses))
                {
                    renderedBookSource = model.Verses;
                    visibleAnchor = model.Position;
                    positionTimer.Stop();
                }
                // Measure the compact/fixed surface while the last native
                // adaptive spread remains readable. Commit after native layout.
                var surface = ++surfaceRevision;
                if (adaptivePresented) { mushafView.Opacity = 0; mushafView.IsHitTestVisible = false; }
                mushafView.Visibility = Visibility.Visible;

                mushafView.Show(
                    model.Verses,
                    model.FacingVerses,
                    model.Settings,
                    CurrentActionVerse?.Id,
                    visibleAnchor ?? model.Position,
                    availableReadingWidth,
                    activePalette,
                    ReadingGrid.ActualHeight > 0 ? ReadingGrid.ActualHeight : 800,
                    lastNavigationForward,
                    pageTurnAnimated,
                    uiSettings.AnimationsEnabled);
                QueueBookPresentation(surface);
                pageTurnAnimated = false;
            }
            else
            {
                continuousBook.CancelPreparation();
                var surface = ++surfaceRevision;
                if (adaptivePresented) ReadingSurface.Opacity = 0;
                ReadingSurface.Visibility = Visibility.Visible;
                var needsCards = !ReferenceEquals(rendered, model.Verses) || cards.Count == 0 || VerseList.Items.Count == 0;
                if (needsCards)
                {
                    visibleAnchor = model.Position;
                    rendered = model.Verses;
                    BuildCards();
                    RestoreAnchor(model.Position);
                }
                foreach (var card in cards)
                {
                    AyahElements.Style(card, model.Settings, model.SelectedVerse?.Id, activePalette);
                }
                VerseList.SelectedItem = cards.FirstOrDefault(c => c.Verse.Id == model.SelectedVerse?.Id)?.Card;
                QueueStudyPresentation(surface);
            }

            UpdateReadingChrome();
            UpdateEdgeDrawerContext();
            UpdateTafsirLayout();

            var sidebarText = IsEdgeDrawer && !model.HasActiveSelection ? "" : model.Tafsir;
            var nextSidebarKey = (CurrentActionVerse?.Id, sidebarText, activePalette.Tokens);
            if (sidebarTafsirKey != nextSidebarKey)
            {
                sidebarTafsirKey = nextSidebarKey;
                TafsirText.Blocks.Clear();
                TafsirText.Blocks.Add(CreateTafsirParagraph(sidebarText, activePalette));
            }

            PopupTitle.Text = model.TafsirTitle;
            SheetTitle.Text = model.TafsirTitle;

            var selectedVerse = CurrentActionVerse;
            var nextScriptureKey = (selectedVerse?.Id, selectedVerse?.Text ?? "", activePalette.Tokens);
            if (supplementalScriptureKey != nextScriptureKey)
            {
                supplementalScriptureKey = nextScriptureKey;
                PopupScriptureText.Blocks.Clear();
                SheetScriptureText.Blocks.Clear();
                if (selectedVerse is not null)
                {
                    PopupScriptureText.Blocks.Add(CreateScriptureParagraph(selectedVerse, activePalette, 24, 44));
                    SheetScriptureText.Blocks.Add(CreateScriptureParagraph(selectedVerse, activePalette, 21, 38));
                }
            }

            var nextTafsirKey = (selectedVerse?.Id, model.Tafsir, activePalette.Tokens);
            if (supplementalTafsirKey != nextTafsirKey)
            {
                supplementalTafsirKey = nextTafsirKey;
                PopupTafsirText.Blocks.Clear();
                PopupTafsirText.Blocks.Add(CreateTafsirParagraph(model.Tafsir, activePalette));
                SheetTafsirText.Blocks.Clear();
                SheetTafsirText.Blocks.Add(CreateTafsirParagraph(model.Tafsir, activePalette));
            }
        }
        finally
        {
            updating = false;
        }
    }

    private void UpdateReadingChrome()
    {
        if (IsContinuousPreview)
        {
            UpdateContinuousChrome();
            UpdateDesktopCommands();
            return;
        }
        AutomationProperties.SetName(NextButton, "Go to next page (left / forward) (Left Arrow / Ctrl+Left)");
        AutomationProperties.SetName(PreviousButton, "Go to previous page (right / backward) (Right Arrow / Ctrl+Right)");
        var isSpread = model.Settings.Mode == ReadingMode.Mushaf && mushafView.IsSpread;
        var spreadStart = ReadingLayout.SpreadStart(model.Page);

        var canGoPrev = isSpread ? spreadStart > 1 : model.Page > 1;
        var canGoNext = isSpread ? spreadStart + 2 <= 604 : model.Page < 604;
        PreviousButton.IsEnabled = model.Ready && !model.IsBusy && canGoPrev;
        NextButton.IsEnabled = model.Ready && !model.IsBusy && canGoNext;

        // In RTL Arabic reading: Next advances leftward (◀); Previous returns rightward (▶)

        ToolTipService.SetToolTip(NextButton, isSpread
            ? (spreadStart + 2 <= 604 ? $"Go to Pages {spreadStart + 2}–{Math.Min(604, spreadStart + 3)} (Left / Forward)" : "End of Qur'an")
            : (model.Page < 604 ? $"Go to Page {model.Page + 1} (Left / Forward)" : "End of Qur'an"));
        ToolTipService.SetToolTip(PreviousButton, isSpread
            ? (spreadStart > 1 ? $"Go to Pages {spreadStart - 2}–{spreadStart - 1} (Right / Backward)" : "Beginning of Qur'an")
            : (model.Page > 1 ? $"Go to Page {model.Page - 1} (Right / Backward)" : "Beginning of Qur'an"));

        if (model.Settings.FitSpread)
        {
            FitToggle.IsChecked = true;
            var fit = mushafView.Fit;
            FitToggle.Content = fit.Category switch
            {
                ReadingFitCategory.ExactFit => "Fit Spread",
                ReadingFitCategory.SmallOverflow => "Fit · small overflow",
                _ => "Fit (Scrollable)"
            };
            ToolTipService.SetToolTip(FitToggle, fit.Category switch
            {
                ReadingFitCategory.ExactFit => "Fit Spread active · No measured vertical overflow (Ctrl+0)",
                ReadingFitCategory.SmallOverflow => $"Fit Spread active · {fit.OverflowDip:0.##} DIP vertical overflow within the existing 8 DIP tolerance; scroll to reach the bottom (Ctrl+0)",
                _ => "Fit Spread active · Scroll to access all content (Ctrl+0)"
            });
        }
        else
        {
            FitToggle.IsChecked = false;
            FitToggle.Content = "Comfort Zoom";
            ToolTipService.SetToolTip(FitToggle, $"Comfort reading zoom ({model.Settings.Scale:0} DIP) · Click to fit spread (Ctrl+0)");
        }
        if (CurrentActionVerse is { } selected)
        {
            var surahLabel = !string.IsNullOrEmpty(selected.SurahName) ? $"سُورَةُ {selected.SurahName}" : "";
            var juzLabel = SurahMetadata.JuzNameArabic(selected.Juz);
            if (isSpread)
            {
                var endPage = Math.Min(604, spreadStart + 1);
                PositionLabel.Text = $"صفحة {spreadStart}–{endPage} · {juzLabel} · {surahLabel} (آية {selected.Id.Ayah})";
            }
            else
            {
                PositionLabel.Text = $"صفحة {model.ActivePage} / 604 · {juzLabel} · {surahLabel} (آية {selected.Id.Ayah})";
            }
            if (!model.CanonicalSelectionLoaded)
                PositionLabel.Text = $"Canonical page {model.Page} · Reading {model.Position.Ayah} · Tafsir selection {selected.Id} (p. {selected.Page})";
        }
        else
        {
            PositionLabel.Text = model.Ready ? $"Canonical page {model.Page} · Reading {model.Position.Ayah} · No ayah selected" : "جاري التحقق من المحتوى…";
        }
        UpdateDesktopCommands();
    }

    private static Paragraph CreateScriptureParagraph(Verse verse, ReaderPalette palette, double fontSize, double lineHeight)
    {
        var paragraph = new Paragraph
        {
            LineHeight = lineHeight,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight
        };
        var run = new Run
        {
            Text = verse.Text,
            FontFamily = AyahElements.Hafs,
            FontSize = fontSize,
            Foreground = palette.ScriptureForeground
        };
        paragraph.Inlines.Add(run);
        return paragraph;
    }

    private static Paragraph CreateTafsirParagraph(string tafsirMarkup, ReaderPalette palette)
    {
        var paragraph = new Paragraph();
        foreach (var span in TafsirFormatter.Parse(tafsirMarkup))
        {
            var run = new Run
            {
                Text = span.Text,
                FontWeight = span.IsAyah ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
                Foreground = span.IsAyah ? palette.AccentColor : palette.TafsirForeground
            };
            if (span.IsAyah)
            {
                // Ayah spans contain Quranic sacred quotations and KFGQPC ornate quotation brackets (U+FD61/U+FD60).
                // Rendering with Hafs font ensures authentic Uthmanic calligraphy and authentic ornate brackets
                // instead of misinterpreting the compatibility codepoints as standard Seen-Meem ligatures.
                run.FontFamily = AyahElements.Hafs;
            }
            paragraph.Inlines.Add(run);
        }
        return paragraph;
    }

    private void BuildCards()
    {
        cards.Clear();
        VerseList.Items.Clear();
        foreach (var verse in model.Verses)
        {
            var element = AyahElements.Create(verse);
            element.Card.AddHandler(UIElement.TappedEvent, new TappedEventHandler(async (_, _) => await model.SelectAsync(verse)), true);
            VerseList.Items.Add(element.Card);
            cards.Add(element);
        }
    }

    private void ApplyPalette()
    {
        var actualTheme = SimulatedSystemTheme ?? Root.ActualTheme;
        activePalette = ReaderPalette.For(model.Settings.Theme, actualTheme);

        if (SimulatedSystemTheme is not null)
        {
            Root.RequestedTheme = SimulatedSystemTheme.Value;
        }
        else
        {
            Root.RequestedTheme = model.Settings.Theme switch
            {
                ReadingTheme.Dark => ElementTheme.Dark,
                ReadingTheme.Light or ReadingTheme.Sepia => ElementTheme.Light,
                ReadingTheme.System => ElementTheme.Default,
                _ => ElementTheme.Default
            };
        }

        Root.Background = activePalette.CanvasBackground;
        ReadingSurface.Background = activePalette.ReadingSurface;
        ReadingSurface.BorderBrush = activePalette.BorderColor;
        TafsirPanel.Background = activePalette.TafsirSurface;
        TafsirPanel.BorderBrush = activePalette.BorderColor;
        TafsirTitle.Foreground = activePalette.ScriptureForeground;
        TafsirAttribution.Foreground = activePalette.MetadataForeground;
        TafsirText.Foreground = activePalette.TafsirForeground;
        TitleLabel.Foreground = activePalette.ScriptureForeground;
        SubtitleLabel.Foreground = activePalette.MetadataForeground;
        PositionLabel.Foreground = activePalette.MetadataForeground;
        ProvisionalNotice.Foreground = activePalette.MetadataForeground;

        // Apply theme to Focused Popup
        TafsirPopupScrim.Background = activePalette.ScrimBackground;
        TafsirPopupCard.Background = activePalette.PopupSurface;
        TafsirPopupCard.BorderBrush = activePalette.BorderColor;
        PopupTitle.Foreground = activePalette.ScriptureForeground;
        PopupAttribution.Foreground = activePalette.MetadataForeground;
        PopupScriptureBox.Background = activePalette.ScriptureBoxSurface;
        PopupScriptureBox.BorderBrush = activePalette.BorderColor;
        PopupScriptureText.Foreground = activePalette.ScriptureForeground;
        PopupDivider.Background = activePalette.SeparatorColor;
        PopupTafsirText.Foreground = activePalette.TafsirForeground;

        // Apply theme to Bottom Sheet
        TafsirBottomSheet.Background = activePalette.PopupSurface;
        TafsirBottomSheet.BorderBrush = activePalette.BorderColor;
        SheetDragPill.Background = activePalette.BorderColor;
        SheetTitle.Foreground = activePalette.ScriptureForeground;
        SheetScriptureBox.Background = activePalette.ScriptureBoxSurface;
        SheetScriptureBox.BorderBrush = activePalette.BorderColor;
        SheetScriptureText.Foreground = activePalette.ScriptureForeground;
        SheetTafsirText.Foreground = activePalette.TafsirForeground;
        ApplyCaptionTheme();
    }

    private void UpdateTafsirLayout()
    {
        readingTail.Height = Math.Max(0, ReadingGrid.ActualHeight);
        var open = model.Settings.TafsirOpen;
        TafsirEdgeColumn.Width = new(IsBookMode ? EdgeRailWidth : 0);
        var totalWidth = Root.ActualWidth > 0 ? Root.ActualWidth : (ReadingGrid.ActualWidth > 0 ? ReadingGrid.ActualWidth : 1200);
        var totalHeight = Root.ActualHeight > 0 ? Root.ActualHeight : 800;

        if (!open)
        {
            TafsirPopupOverlay.Visibility = Visibility.Collapsed;
            TafsirBottomSheet.Visibility = Visibility.Collapsed;
            TafsirPanel.Visibility = Visibility.Collapsed;
            TafsirColumn.Width = new GridLength(0);
            return;
        }

        // When open, determine presentation:
        // In Mushaf Mode: use model.Settings.Presentation (FocusedPopup = default)
        // In Tafsir Study Mode: use TafsirPanel (Study Workspace)
        if (IsBookMode)
        {
            switch (model.Settings.Presentation)
            {
                case TafsirPresentation.FocusedPopup:
                    {
                        var wasVisible = TafsirPopupOverlay.Visibility == Visibility.Visible;
                        TafsirPopupOverlay.Visibility = Visibility.Visible;
                        TafsirBottomSheet.Visibility = Visibility.Collapsed;
                        TafsirPanel.Visibility = Visibility.Collapsed;
                        TafsirColumn.Width = new GridLength(0);

                        // Responsive dimensions
                        double popupWidth;
                        if (totalWidth >= 1200) popupWidth = Math.Min(780, totalWidth - 100);
                        else if (totalWidth >= 750) popupWidth = Math.Min(700, totalWidth - 50);
                        else popupWidth = Math.Max(300, totalWidth - 24);

                        TafsirPopupCard.Width = popupWidth;
                        TafsirPopupCard.MaxHeight = Math.Max(320, totalHeight - 64);

                        if (!wasVisible)
                        {
                            AnimateElement(TafsirPopupOverlay);
                            PopupCloseButton.Focus(FocusState.Programmatic);
                        }
                        break;
                    }
                case TafsirPresentation.BottomSheet:
                    {
                        var wasVisible = TafsirBottomSheet.Visibility == Visibility.Visible;
                        TafsirPopupOverlay.Visibility = Visibility.Collapsed;
                        TafsirBottomSheet.Visibility = Visibility.Visible;
                        TafsirPanel.Visibility = Visibility.Collapsed;
                        TafsirColumn.Width = new GridLength(0);

                        TafsirBottomSheet.MaxHeight = Math.Min(420, Math.Max(260, totalHeight * 0.45));
                        if (!wasVisible)
                        {
                            AnimateElement(TafsirBottomSheet);
                            SheetCloseButton.Focus(FocusState.Programmatic);
                        }
                        break;
                    }
                case TafsirPresentation.EdgeDrawer:
                case TafsirPresentation.SlidingSidebar:
                default:
                    {
                        TafsirPopupOverlay.Visibility = Visibility.Collapsed;
                        TafsirBottomSheet.Visibility = Visibility.Collapsed;
                        ConfigureSlidingSidebar(totalWidth);
                        break;
                    }
            }
        }
        else
        {
            // Tafsir Study Mode maintains dedicated workspace with TafsirPanel
            TafsirPopupOverlay.Visibility = Visibility.Collapsed;
            TafsirBottomSheet.Visibility = Visibility.Collapsed;
            ConfigureSlidingSidebar(totalWidth);
        }
    }

    private void ConfigureSlidingSidebar(double totalWidth)
    {
        var wasVisible = TafsirPanel.Visibility == Visibility.Visible;
        TafsirPanel.Visibility = Visibility.Visible;

        var drawerWidth = Math.Clamp(model.Settings.DrawerWidth, 300, 560);
        var effectiveScale = mushafView.CurrentScale > 0 ? mushafView.CurrentScale : model.Settings.Scale;
        var spreadThreshold = ReadingLayout.SpreadThreshold(effectiveScale);
        var singlePageThreshold = ReadingLayout.SinglePageThreshold(effectiveScale);
        var readingWidth = (ReadingGrid.ActualWidth > 0 ? ReadingGrid.ActualWidth : totalWidth) - (IsBookMode ? EdgeRailWidth : 0);

        // Priority Four: Three-Tier Intelligent Layout
        // Tier 1: Wide Desktop (Spread + Docked Drawer)
        bool canDockSpread = readingWidth >= (spreadThreshold + drawerWidth + 30);

        // Tier 2: Medium Desktop (Adaptive Single Page + Docked Drawer)
        bool wasSpread = readingWidth >= spreadThreshold;
        bool canDockSingle = !wasSpread && (readingWidth >= singlePageThreshold + drawerWidth + 24);

        bool canDock;
        if (IsBookMode)
        {
            canDock = canDockSpread || canDockSingle;
            if (IsContinuousPreview && !IsEdgeDrawer) canDock = false;
        }
        else
        {
            canDock = readingWidth >= (600 + drawerWidth + 24);
        }

        ReadingGrid.ColumnSpacing = 0;
        TafsirColumn.Width = canDock ? new GridLength(drawerWidth + 20) : new GridLength(0);
        TafsirPanel.Margin = canDock ? new(20, 0, 0, 0) : new(0);

        var overlayAlignment = HorizontalAlignment.Right;
        if (!canDock && IsBookMode && !IsEdgeDrawer)
        {
            var isRightSelected = IsContinuousPreview && !IsPreviewOpening ? continuousBook.IsRightSelected(model.SelectedVerse?.Id) : mushafView.IsRightPageSelected(model.SelectedVerse?.Id);
            overlayAlignment = isRightSelected ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        }

        Grid.SetColumn(TafsirPanel, canDock ? 1 : 0);
        TafsirPanel.HorizontalAlignment = canDock ? HorizontalAlignment.Stretch : overlayAlignment;
        TafsirPanel.Width = canDock ? double.NaN : Math.Min(drawerWidth, Math.Max(280, readingWidth - 24));
        TafsirText.Width = Math.Max(180, (canDock ? drawerWidth : TafsirPanel.Width) - 44);
        Canvas.SetZIndex(TafsirPanel, canDock ? 0 : 100);

        if (!wasVisible)
        {
            if (IsEdgeDrawer) edgeMotion.Enter(TafsirPanel, (float)drawerWidth, MotionEnabled);
            else AnimateElement(TafsirPanel);
        }
    }

    private void AnimateElement(UIElement element)
    {
        activeAnimation?.Stop();
        activeAnimation = null;

        var durationMs = ReadingLayout.MotionDuration(uiSettings.AnimationsEnabled, model.Settings.ReduceMotion);
        if (durationMs <= 0)
        {
            element.Opacity = 1.0;
            return;
        }

        var sb = new Storyboard();
        var fadeIn = new DoubleAnimation
        {
            From = 0.0,
            To = 1.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(durationMs))
        };
        Storyboard.SetTarget(fadeIn, element);
        Storyboard.SetTargetProperty(fadeIn, "Opacity");
        sb.Children.Add(fadeIn);

        activeAnimation = sb;
        sb.Completed += (_, _) => { activeAnimation = null; };
        sb.Begin();
    }

    private ReadingPosition CaptureAnchor()
    {
        if (IsContinuousPreview && !IsPreviewOpening) return continuousBook.CaptureAnchor();
        if (IsBookMode)
        {
            return mushafView.CaptureAnchor();
        }

        if (scroller is null || !scroller.IsLoaded || scroller.XamlRoot is null) return model.Position;
        try
        {
            foreach (var card in cards)
            {
                if (VerseList.ContainerFromItem(card.Card) is not ListViewItem container) continue;
                if (!IsVisualAttached(container, scroller)) continue;
                var y = container.TransformToVisual(scroller).TransformPoint(new Point()).Y;
                if (y + container.ActualHeight > 0) return new(card.Verse.Id, y);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // Container detached or recycled during layout transition
        }
        return model.Position;
    }

    private async void RestoreAnchor(ReadingPosition anchor)
    {
        if (closing) return;
        var revision = ++layoutRevision;
        layoutChanging = true;

        try
        {
            if (IsContinuousPreview && !IsPreviewOpening)
            {
                await continuousBook.RestoreAnchorAsync(anchor);
                if (revision == layoutRevision) visibleAnchor = continuousBook.CaptureAnchor();
                return;
            }
            if (IsBookMode)
            {
                await mushafView.RestoreAnchorAsync(anchor);
                if (revision == layoutRevision)
                {
                    visibleAnchor = mushafView.CaptureAnchor();
                }
                return;
            }

            var card = cards.FirstOrDefault(c => c.Verse.Id == anchor.Ayah)?.Card;
            if (card is null) return;
            VerseList.ScrollIntoView(card, ScrollIntoViewAlignment.Leading);
            await NextLayoutAsync();
            if (revision != layoutRevision) return;
            scroller ??= FindVisual<ScrollViewer>(VerseList);
            if (scroller is not null && VerseList.ContainerFromItem(card) is ListViewItem container)
            {
                if (IsVisualAttached(container, scroller))
                {
                    try
                    {
                        var y = container.TransformToVisual(scroller).TransformPoint(new Point()).Y;
                        if (anchor.TextOffset > 0 && cards.FirstOrDefault(c => c.Verse.Id == anchor.Ayah) is { } entry)
                        {
                            ContinuousScripture.RequireBoundary(entry.Verse.Text, anchor.TextOffset);
                            var start = entry.Text.Inlines.OfType<Run>().FirstOrDefault()?.ContentStart ?? entry.Text.ContentStart;
                            var pointer = start.GetPositionAtOffset(anchor.TextOffset, LogicalDirection.Forward);
                            if (pointer is not null)
                                y = entry.Text.TransformToVisual(scroller).TransformPoint(new()).Y + pointer.GetCharacterRect(LogicalDirection.Forward).Y;
                        }
                        scroller.ChangeView(null, Math.Max(0, scroller.VerticalOffset + y - anchor.Offset), null, true);
                    }
                    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                    {
                        // Container unattached or layout in flight
                    }
                }
            }
            await NextLayoutAsync();
            if (revision == layoutRevision)
            {
                visibleAnchor = CaptureAnchor();
            }
        }
        finally
        {
            if (revision == layoutRevision)
            {
                layoutChanging = false;
            }
        }
    }

    private Task NextLayoutAsync()
    {
        var completion = new TaskCompletionSource();
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (!closing && Root.IsLoaded && Root.XamlRoot is not null) Root.UpdateLayout();
            completion.SetResult();
        });
        return completion.Task;
    }

    private async Task ChangeSettingsAsync(ReaderSettings settings)
    {
        var request = ++settingsRequestRevision;
        if (pendingSettingsChanges == 0 && settings == model.Settings) return;
        pendingSettingsChanges++;
        try
        {
            var anchor = CaptureAnchor();
            if (settings.Mode == ReadingMode.AdaptiveBook && !IsContinuousPreview) anchor = CaptureContinuousEntryAnchor(anchor);
            layoutChanging = true;
            if (IsContinuousPreview || settings.Mode == ReadingMode.AdaptiveBook) await model.RememberPositionAsync(anchor.Ayah, anchor.Offset, anchor.TextOffset);
            if (request != settingsRequestRevision) return;
            await model.UpdateSettingsAsync(settings);
            if (request != settingsRequestRevision) return;
            RestoreAnchor(anchor);
        }
        finally { pendingSettingsChanges--; }
    }

    private void ViewChanged(object? sender, ScrollViewerViewChangedEventArgs args)
    {
        if (layoutChanging || args.IsIntermediate) return;
        visibleAnchor = CaptureAnchor();
        positionTimer.Stop();
        positionTimer.Start();
    }

    private async void SaveVisiblePosition(object? sender, object args)
    {
        positionTimer.Stop();
        if (layoutChanging || model.IsBusy || IsContinuousPreview && !IsPreviewOpening && !continuousBook.Ready) return;
        var anchor = visibleAnchor ?? CaptureAnchor();
        await model.RememberPositionAsync(anchor.Ayah, anchor.Offset, anchor.TextOffset);
    }

    private void ReadingSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (updating || closing || !model.Ready) return;
        UpdateTafsirLayout();
        if (IsContinuousPreview && !IsPreviewOpening)
        {
            if (model.Continuous is { } source)
                continuousBook.Show(source, model.Settings, CurrentActionVerse?.Id, activePalette,
                    Math.Max(300, ReadingArea.ActualWidth), ReadingGrid.ActualHeight);
            UpdateReadingChrome();
            return;
        }
        if (IsBookMode)
        {
            var isSidebarPresentation = model.Settings.Presentation is TafsirPresentation.SlidingSidebar or TafsirPresentation.EdgeDrawer;
            var drawerWidth = Math.Clamp(model.Settings.DrawerWidth, 300, 560);
            var effectiveScale = mushafView.CurrentScale > 0 ? mushafView.CurrentScale : model.Settings.Scale;
            var spreadThreshold = ReadingLayout.SpreadThreshold(effectiveScale);
            var singlePageThreshold = ReadingLayout.SinglePageThreshold(effectiveScale);
            var totalGridWidth = ReadingGrid.ActualWidth > 0 ? ReadingGrid.ActualWidth - (IsBookMode ? EdgeRailWidth : 0) : 1200;

            var canDockSpread = totalGridWidth >= (spreadThreshold + drawerWidth + 30);
            var wasSpread = totalGridWidth >= spreadThreshold;
            var canDockSingle = !wasSpread && (totalGridWidth >= singlePageThreshold + drawerWidth + 24);
            var canDock = isSidebarPresentation && model.Settings.TafsirOpen && (canDockSpread || canDockSingle);
            ReadingGrid.ColumnSpacing = 0;

            var availableReadingWidth = totalGridWidth;
            if (canDock) availableReadingWidth = Math.Max(300, availableReadingWidth - drawerWidth - 20);

            mushafView.Show(
                model.Verses,
                model.FacingVerses,
                model.Settings,
                CurrentActionVerse?.Id,
                visibleAnchor ?? model.Position,
                availableReadingWidth,
                activePalette,
                ReadingGrid.ActualHeight > 0 ? ReadingGrid.ActualHeight : 800,
                isForwardTurn: true,
                animate: false);
            UpdateReadingChrome();
        }
    }

    private void XamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        if (updating || closing || !model.Ready || layoutChanging) return;
        RestoreAnchor(visibleAnchor ?? model.Position);
    }

    private async void VerseSelected(object sender, SelectionChangedEventArgs args)
    {
        if (!updating && VerseList.SelectedItem is Grid { Tag: Verse verse })
            await model.SelectAsync(verse);
    }

    private async void SurahChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!updating && SurahPicker.SelectedItem is Surah surah)
        {
            NavigationTrace.Begin("surah", surah.Number.ToString(System.Globalization.CultureInfo.InvariantCulture));
            NavigationTrace.Begin("surah", surah.Number.ToString(System.Globalization.CultureInfo.InvariantCulture));
            pageTurnAnimated = false;
            await model.GoToAsync(new(surah.Number, 1), activateSelection: false);
        }
    }

    private async void GoPage(object sender, RoutedEventArgs args)
    {
        if (int.TryParse(PagePicker.Text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var page) && page is >= 1 and <= 604)
        {
            NavigationTrace.Begin("page", page.ToString(System.Globalization.CultureInfo.InvariantCulture));
            pageTurnAnimated = false;
            NavigationTrace.Begin("page", page.ToString(System.Globalization.CultureInfo.InvariantCulture));
            await model.NavigateAsync(page);
        }
        else
        {
            ErrorBar.IsOpen = true;
            ErrorBar.Message = "Enter a page number from 1 to 604.";
        }
    }

    private void PageKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Enter)
        {
            GoPage(sender, new());
            args.Handled = true;
        }
    }

    private int? pendingNavigationTarget;

    private void OnRootPreviewKeyDown(object sender, KeyRoutedEventArgs args)
    {
        pointerTurnSource = null;
        if (args.Key is not (VirtualKey.Left or VirtualKey.Right)) return;

        // Verify no modifier keys are pressed (Ctrl, Alt, Shift)
        var ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
        var alt = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu);
        var shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
        if (ctrl.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down) ||
            alt.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down) ||
            shift.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
        {
            return;
        }

        // Guard against intercepting arrow keys in text inputs or open dropdowns
        if (!IsArrowNavigationAllowed()) return;

        // MRG-003 Right-to-Left keyboard mapping:
        // Left Arrow = Next spread / page (forward reading in RTL)
        // Right Arrow = Previous spread / page (backward reading in RTL)
        if (args.Key == VirtualKey.Left)
        {
            NavigationTrace.Input("key-next");
            NextPage(sender, new RoutedEventArgs());
            args.Handled = true;
        }
        else if (args.Key == VirtualKey.Right)
        {
            NavigationTrace.Input("key-previous");
            PreviousPage(sender, new RoutedEventArgs());
            args.Handled = true;
        }
    }

    private bool IsArrowNavigationAllowed()
    {
        if (!model.Ready || !IsBookMode) return false;
        // 1. Tafsir popup overlay is open
        if (model.Settings.TafsirOpen && TafsirPopupOverlay.Visibility == Visibility.Visible)
        {
            return false;
        }

        // 2. Dropdowns are open
        if (SurahPicker.IsDropDownOpen || ModePicker.IsDropDownOpen ||
            ThemePicker.IsDropDownOpen || TafsirPresentationPicker.IsDropDownOpen)
        {
            return false;
        }

        // 3. User focus is in a text input (e.g. PagePicker)
        if (Root.XamlRoot is not null)
        {
            var focused = FocusManager.GetFocusedElement(Root.XamlRoot) as DependencyObject;
            for (var element = focused; element is not null; element = VisualTreeHelper.GetParent(element))
            {
                if (ReferenceEquals(element, TafsirPanel) || ReferenceEquals(element, TafsirBottomSheet)) return false;
                if (element is TextBox or RichEditBox or PasswordBox or ComboBox or Slider or NumberBox)
                    return false;
            }
            if (VisualTreeHelper.GetOpenPopupsForXamlRoot(Root.XamlRoot).Any(p => p.IsOpen && p.Child is not ToolTip)) return false;
        }

        return true;
    }

    private async void NextPage(object sender, RoutedEventArgs args)
    {
        if (!AcceptPageActivation(sender, true)) { NavigationTrace.Mark(0, "CommandRejected", "next pointer duplicate"); return; }
        var navigationTrace = NavigationTrace.Begin("next");
        if (IsContinuousPreview)
        {
            if (CancelPendingOpeningNavigation()) return;
            if (IsPreviewOpening)
            {
                var openingTarget = new AyahId(2, mushafView.IsSpread || model.Position.Ayah.Surah == 2 ? 6 : 1);
                if (navigationTrace != 0) NavigationTrace.Mark(navigationTrace, "TargetSourceResolved", openingTarget.ToString());
                await (adaptiveNavigation = model.GoToAsync(openingTarget, activateSelection: false));
            }
            else { continuousBook.AnimationsEnabled = uiSettings.AnimationsEnabled; continuousBook.Turn(true); }
            return;
        }
        var isSpread = model.Settings.Mode == ReadingMode.Mushaf && mushafView.IsSpread;
        var currentBase = pendingNavigationTarget ?? model.Page;
        if (isSpread && ReadingLayout.SpreadStart(currentBase) >= 603) return;
        var target = ReadingLayout.NextPage(currentBase, isSpread);
        if (target == currentBase) return; // Clamped at boundary (page 604)

        pendingNavigationTarget = target;
        lastNavigationForward = true;
        pageTurnAnimated = true;
        try
        {
            await model.NavigateAsync(target);
        }
        finally
        {
            if (pendingNavigationTarget == target)
            {
                pendingNavigationTarget = null;
            }
        }
    }

    private async void PreviousPage(object sender, RoutedEventArgs args)
    {
        if (!AcceptPageActivation(sender, false)) { NavigationTrace.Mark(0, "CommandRejected", "previous pointer duplicate"); return; }
        var navigationTrace = NavigationTrace.Begin("previous");
        if (IsContinuousPreview)
        {
            if (CancelPendingOpeningNavigation()) return;
            if (IsPreviewOpening)
            {
                if (!mushafView.IsSpread && model.Position.Ayah.Surah == 2) await (adaptiveNavigation = model.GoToAsync(new(1, 1), activateSelection: false));
            }
            else { continuousBook.AnimationsEnabled = uiSettings.AnimationsEnabled; continuousBook.Turn(false); }
            return;
        }
        var isSpread = model.Settings.Mode == ReadingMode.Mushaf && mushafView.IsSpread;
        var currentBase = pendingNavigationTarget ?? model.Page;
        if (isSpread && ReadingLayout.SpreadStart(currentBase) <= 1) return;
        var target = ReadingLayout.PreviousPage(currentBase, isSpread);
        if (target == currentBase) return; // Clamped at boundary (page 1)

        pendingNavigationTarget = target;
        lastNavigationForward = false;
        pageTurnAnimated = true;
        try
        {
            await model.NavigateAsync(target);
        }
        finally
        {
            if (pendingNavigationTarget == target)
            {
                pendingNavigationTarget = null;
            }
        }
    }

    private async void ThemeChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!updating && ThemePicker.SelectedIndex >= 0)
            await ChangeSettingsAsync(model.Settings with { Theme = (ReadingTheme)ThemePicker.SelectedIndex });
    }

    private async void ModeChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!updating && ModePicker.SelectedIndex >= 0)
            await ChangeSettingsAsync(model.Settings with { Mode = (ReadingMode)ModePicker.SelectedIndex });
    }

    private async void Smaller(object sender, RoutedEventArgs args) =>
        await ChangeSettingsAsync(model.Settings with { Scale = Math.Max(ReadingLayout.MinComfortableScale, model.Settings.Scale - 2), FitSpread = false });

    private async void Larger(object sender, RoutedEventArgs args) =>
        await ChangeSettingsAsync(model.Settings with { Scale = Math.Min(ReadingLayout.MaxManualScale, model.Settings.Scale + 2), FitSpread = false });

    private async void ToggleFitSpread(object sender, RoutedEventArgs args) =>
        await ChangeSettingsAsync(model.Settings with { FitSpread = !model.Settings.FitSpread });

    private void FitKey(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (IsContinuousPreview) { args.Handled = true; return; }
        ToggleFitSpread(sender, new());
        args.Handled = true;
    }

    private async void ToggleTafsir(object sender, RoutedEventArgs args) =>
        await ChangeSettingsAsync(model.Settings with { TafsirOpen = !model.Settings.TafsirOpen });

    private async void TogglePin(object sender, RoutedEventArgs args) =>
        await ChangeSettingsAsync(model.Settings with { DrawerPinned = !model.Settings.DrawerPinned });

    private async void CycleDrawerWidth(object sender, RoutedEventArgs args)
    {
        var nextWidth = model.Settings.DrawerWidth >= 500 ? 340.0 : model.Settings.DrawerWidth + 60.0;
        await ChangeSettingsAsync(model.Settings with { DrawerWidth = nextWidth });
    }

    private async void CloseTafsir(object sender, RoutedEventArgs args)
    {
        var edge = IsEdgeDrawer;
        if (edge && model.Settings.TafsirOpen)
        {
            edgeClosing = true;
            if (!await edgeMotion.ExitAsync(TafsirPanel, (float)Math.Max(300, TafsirPanel.ActualWidth), MotionEnabled)) return;
            edgeClosing = false;
        }
        await ChangeSettingsAsync(model.Settings with { TafsirOpen = false });
        if (edge) TafsirToggle.Focus(FocusState.Programmatic);
        else if (IsContinuousPreview && !IsPreviewOpening) continuousBook.FocusActive();
        else if (IsBookMode)
        {
            mushafView.FocusActive();
        }
        else
        {
            VerseList.Focus(FocusState.Programmatic);
        }
    }

    private async void OnPopupScrimTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs args)
    {
        await ChangeSettingsAsync(model.Settings with { TafsirOpen = false });
        if (IsContinuousPreview && !IsPreviewOpening) continuousBook.FocusActive();
        else if (IsBookMode)
        {
            mushafView.FocusActive();
        }
    }

    private async void CyclePresentationStyle(object sender, RoutedEventArgs args)
    {
        var current = model.Settings.Presentation;
        var next = current switch
        {
            TafsirPresentation.FocusedPopup => TafsirPresentation.SlidingSidebar,
            TafsirPresentation.SlidingSidebar => TafsirPresentation.BottomSheet,
            TafsirPresentation.BottomSheet => TafsirPresentation.FocusedPopup,
            TafsirPresentation.EdgeDrawer => TafsirPresentation.FocusedPopup,
            _ => TafsirPresentation.FocusedPopup
        };
        await ChangeSettingsAsync(model.Settings with { Presentation = next });
    }

    private async void TafsirPresentationChanged(object sender, SelectionChangedEventArgs args)
    {
        if (updating || TafsirPresentationPicker.SelectedIndex < 0) return;
        var presentation = (TafsirPresentation)TafsirPresentationPicker.SelectedIndex;
        if (model.Settings.Presentation != presentation)
        {
            await ChangeSettingsAsync(model.Settings with { Presentation = presentation });
        }
    }

    private void OnPopupKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == Windows.System.VirtualKey.Escape)
        {
            CloseTafsir(sender, new());
            args.Handled = true;
            return;
        }

        if (args.Key == Windows.System.VirtualKey.Tab)
        {
            var isShift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            var focused = FocusManager.GetFocusedElement(Root.XamlRoot) as DependencyObject;

            if (!isShift)
            {
                if (ReferenceEquals(focused, PopupCloseButton))
                {
                    PopupStyleButton.Focus(FocusState.Keyboard);
                    args.Handled = true;
                }
                else if (ReferenceEquals(focused, PopupStyleButton))
                {
                    PopupContentScrollViewer.Focus(FocusState.Keyboard);
                    args.Handled = true;
                }
                else
                {
                    PopupCloseButton.Focus(FocusState.Keyboard);
                    args.Handled = true;
                }
            }
            else
            {
                if (ReferenceEquals(focused, PopupCloseButton))
                {
                    PopupContentScrollViewer.Focus(FocusState.Keyboard);
                    args.Handled = true;
                }
                else if (ReferenceEquals(focused, PopupContentScrollViewer))
                {
                    PopupStyleButton.Focus(FocusState.Keyboard);
                    args.Handled = true;
                }
                else
                {
                    PopupCloseButton.Focus(FocusState.Keyboard);
                    args.Handled = true;
                }
            }
        }
    }

    private void OnSheetKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == Windows.System.VirtualKey.Escape)
        {
            CloseTafsir(sender, new());
            args.Handled = true;
            return;
        }

        if (args.Key == Windows.System.VirtualKey.Tab)
        {
            var isShift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            var focused = FocusManager.GetFocusedElement(Root.XamlRoot) as DependencyObject;

            if (!isShift)
            {
                if (ReferenceEquals(focused, SheetCloseButton))
                {
                    SheetStyleButton.Focus(FocusState.Keyboard);
                    args.Handled = true;
                }
                else if (ReferenceEquals(focused, SheetStyleButton))
                {
                    SheetContentScrollViewer.Focus(FocusState.Keyboard);
                    args.Handled = true;
                }
                else
                {
                    SheetCloseButton.Focus(FocusState.Keyboard);
                    args.Handled = true;
                }
            }
            else
            {
                if (ReferenceEquals(focused, SheetCloseButton))
                {
                    SheetContentScrollViewer.Focus(FocusState.Keyboard);
                    args.Handled = true;
                }
                else if (ReferenceEquals(focused, SheetContentScrollViewer))
                {
                    SheetStyleButton.Focus(FocusState.Keyboard);
                    args.Handled = true;
                }
                else
                {
                    SheetCloseButton.Focus(FocusState.Keyboard);
                    args.Handled = true;
                }
            }
        }
    }

    private async void Bookmark(object sender, RoutedEventArgs args)
    {
        if (CurrentActionVerse is not null) await model.ToggleBookmarkAsync();
    }

    private void ShowBookmarks(object sender, RoutedEventArgs args)
    {
        var menu = new MenuFlyout { AreOpenCloseAnimationsEnabled = !model.Settings.ReduceMotion && uiSettings.AnimationsEnabled };
        foreach (var id in model.Bookmarks)
        {
            var item = new MenuFlyoutItem { Text = $"Ayah {id}", MinHeight = 40 };
            item.Click += async (_, _) =>
            {
                pageTurnAnimated = false;
                await model.GoToAsync(id);
            };
            menu.Items.Add(item);
        }
        if (menu.Items.Count == 0) menu.Items.Add(new MenuFlyoutItem { Text = "No bookmarks yet", IsEnabled = false, MinHeight = 40 });
        menu.ShowAt(BookmarksButton);
    }

    private void NextPageKey(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (sender.Key == VirtualKey.Left && !IsArrowNavigationAllowed()) return;
        NextPage(sender, new()); args.Handled = true;
    }
    private void PreviousPageKey(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (sender.Key == VirtualKey.Right && !IsArrowNavigationAllowed()) return;
        PreviousPage(sender, new()); args.Handled = true;
    }
    private void BookmarkKey(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) { Bookmark(sender, new()); args.Handled = true; }
    private void TafsirKey(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) { if (IsBookMode) ToggleEdgeTafsir(sender, new()); else ToggleTafsir(sender, new()); args.Handled = true; }
    private async void ModeKey(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        var next = model.Settings.Mode == ReadingMode.Tafsir ? ReadingMode.Mushaf : ReadingMode.Tafsir;
        await ChangeSettingsAsync(model.Settings with { Mode = next });
    }
    private void EscapeKey(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (model.Settings.TafsirOpen)
        {
            CloseTafsir(sender, new());
            if (args is not null) args.Handled = true;
        }
        else if (IsContinuousPreview && (CancelPendingOpeningNavigation() || continuousBook.CancelToVisible()))
        {
            if (args is not null) args.Handled = true;
        }
    }

    private bool CancelPendingOpeningNavigation()
    {
        // A parked adaptive bank can still be Ready. The visible compact
        // opening, rather than that older invisible bank, owns cancellation.
        if (adaptivePresented || !IsContinuousPreview || !continuousBook.IsPreparing || mushafView.Visibility != Visibility.Visible) return false;
        var anchor = mushafView.CaptureAnchor();
        var verse = mushafView.RightPage.Elements.Concat(mushafView.LeftPage.Elements).FirstOrDefault(e => e.Verse.Id == anchor.Ayah)?.Verse
            ?? throw new ResourceException("The displayed opening anchor has no verified source verse.");
        continuousBook.CancelPreparation();
        adaptiveNavigation = model.NavigateAsync(verse.Page, anchor.Ayah, anchor.Offset, anchor.TextOffset, activateTarget: false);
        return true;
    }
    private void FullscreenKey(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        var anchor = CaptureAnchor();
        AppWindow.SetPresenter(AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen ? AppWindowPresenterKind.Overlapped : AppWindowPresenterKind.FullScreen);
        RestoreAnchor(anchor);
        args.Handled = true;
    }

    private async void WindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (closing) return;
        args.Cancel = true;
        closing = true;
        positionTimer.Stop();
        var anchor = CaptureAnchor();
        await model.RememberPositionAsync(anchor.Ayah, anchor.Offset, anchor.TextOffset);
        if (model.Error is not null)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = Root.XamlRoot,
                Title = "Reading state could not be saved",
                Content = model.Error,
                PrimaryButtonText = "Close",
                CloseButtonText = "Stay"
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                closing = false;
                Render();
                return;
            }
        }
        if (scroller is not null) scroller.ViewChanged -= ViewChanged;
        Root.XamlRoot.Changed -= XamlRootChanged;
        positionTimer.Tick -= SaveVisiblePosition;
        model.PropertyChanged -= ModelChanged;
        continuousBook.Clear();
        model.Dispose();
        content.Dispose();
        fontResource.Dispose();
        await NavigationTrace.FinishAsync();
        Close();
    }

    private static T? FindVisual<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T value) return value;
            if (FindVisual<T>(child) is { } nested) return nested;
        }
        return null;
    }
}
