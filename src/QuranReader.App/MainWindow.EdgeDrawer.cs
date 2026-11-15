using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using QuranReader.Core;

namespace QuranReader.App;

public sealed partial class MainWindow
{
    private const double EdgeRailWidth = 16; // existing outer inset and page-frame padding keep the 44-DIP tab clear of scripture
    private Verse? CurrentActionVerse => IsEdgeDrawer ? model.ActiveTafsirVerse : model.SelectedVerse;
    private bool IsEdgeDrawer => IsBookMode && model.Settings.Presentation == TafsirPresentation.EdgeDrawer;
    private readonly ReaderTranslationMotion edgeMotion = new();
    private bool edgeClosing;
    private AyahId? lastEdgeSelection;
    private ThemePaletteTokens? controlTokens;
    private (AyahId? Ayah, bool Edge, ThemePaletteTokens Tokens)? scriptureKey;
    private bool MotionEnabled => !model.Settings.ReduceMotion && uiSettings.AnimationsEnabled;

    private async void ToggleEdgeTafsir(object sender, RoutedEventArgs args)
    {
        if (!IsBookMode) { ToggleTafsir(sender, args); return; }
        if (edgeClosing)
        {
            edgeClosing = false;
            edgeMotion.Stop();
            return;
        }
        if (IsEdgeDrawer && model.Settings.TafsirOpen) { CloseTafsir(sender, args); return; }
        await ChangeSettingsAsync(model.Settings with { Presentation = TafsirPresentation.EdgeDrawer, TafsirOpen = true });
        TafsirToggle.Focus(FocusState.Programmatic);
    }

    private void UpdateEdgeDrawerContext()
    {
        TafsirEdgeColumn.Width = new(IsBookMode ? EdgeRailWidth : 0);
        TafsirToggle.Visibility = IsBookMode ? Visibility.Visible : Visibility.Collapsed;
        var expanded = IsEdgeDrawer && model.Settings.TafsirOpen;
        TafsirToggle.IsChecked = expanded;
        TafsirToggle.IsEnabled = model.Ready;
        TafsirEdgeChevron.Glyph = expanded ? "\uE76C" : "\uE76B";
        AutomationProperties.SetName(TafsirToggle, expanded ? "Collapse Tafsir edge drawer" : "Open Tafsir edge drawer");
        ToolTipService.SetToolTip(TafsirToggle, expanded ? "Collapse Tafsir (Esc or Ctrl+T)" : "Tafsir — open the right-edge drawer (Ctrl+T)");
        var selected = IsEdgeDrawer ? model.ActiveTafsirVerse : model.SelectedVerse;
        if (IsEdgeDrawer && selected is null) TafsirTitle.Text = "Tafsir al-Muyassar";
        EdgeEmptyState.Visibility = IsEdgeDrawer && selected is null ? Visibility.Visible : Visibility.Collapsed;
        SidebarScriptureBox.Visibility = EdgeAyahIdentity.Visibility = IsEdgeDrawer && selected is not null ? Visibility.Visible : Visibility.Collapsed;
        TafsirText.Visibility = IsEdgeDrawer && selected is null ? Visibility.Collapsed : Visibility.Visible;
        EdgeAyahIdentity.Text = selected is null ? "" : $"Ayah {selected.Id} · {selected.SurahName} · canonical page {selected.Page}";
        if (selected?.Id != lastEdgeSelection)
        {
            lastEdgeSelection = selected?.Id;
            edgeMotion.Stop();
            edgeClosing = false;
            SidebarScroll.ChangeView(null, 0, null, true);
        }
        var nextKey = (selected?.Id, IsEdgeDrawer, activePalette.Tokens);
        if (scriptureKey != nextKey)
        {
            scriptureKey = nextKey;
            SidebarScriptureText.Blocks.Clear();
            if (IsEdgeDrawer && selected is not null)
                SidebarScriptureText.Blocks.Add(CreateScriptureParagraph(selected, activePalette, 24, 44));
        }
        if (ReferenceEquals(controlTokens, activePalette.Tokens)) return;
        controlTokens = activePalette.Tokens;
        SidebarScriptureBox.Background = activePalette.ScriptureBoxSurface;
        SidebarScriptureBox.BorderBrush = activePalette.BorderColor;
        EdgeAyahIdentity.Foreground = activePalette.MetadataForeground;
        foreach (var label in EdgeEmptyState.Children.OfType<TextBlock>()) label.Foreground = activePalette.TafsirForeground;
        TafsirToggle.Background = activePalette.ToolbarSurface;
        TafsirToggle.BorderBrush = activePalette.AccentColor;
        TafsirToggle.Foreground = activePalette.ScriptureForeground;
        foreach (var toggle in new[] { TafsirToggle, PinButton })
        {
            toggle.Resources["ToggleButtonBackgroundChecked"] = activePalette.SelectedSurface;
            toggle.Resources["ToggleButtonBackgroundCheckedPointerOver"] = activePalette.HoverSurface;
            toggle.Resources["ToggleButtonBackgroundCheckedPressed"] = activePalette.SelectedSurface;
            toggle.Resources["ToggleButtonForegroundChecked"] = activePalette.ScriptureForeground;
            toggle.Resources["ToggleButtonForegroundCheckedPointerOver"] = activePalette.ScriptureForeground;
            toggle.Resources["ToggleButtonBorderBrushChecked"] = activePalette.AccentColor;
        }
        if (PinButton.Content is not FontIcon) PinButton.Content = new FontIcon { Glyph = "\uE718", FontSize = 16 };
        foreach (var control in new Control[] { NextButton, PreviousButton })
        {
            control.Background = activePalette.ToolbarSurface;
            control.Foreground = activePalette.ScriptureForeground;
            control.BorderBrush = activePalette.AccentColor;
            control.Resources["ButtonBackgroundPointerOver"] = activePalette.HoverSurface;
            control.Resources["ButtonBackgroundPressed"] = activePalette.SelectedSurface;
            control.Resources["ButtonForegroundPointerOver"] = activePalette.ScriptureForeground;
            control.Resources["ButtonForegroundPressed"] = activePalette.ScriptureForeground;
        }
    }
}
