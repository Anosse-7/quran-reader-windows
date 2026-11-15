using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using QuranReader.Core;
using Windows.ApplicationModel.DataTransfer;

namespace QuranReader.App;

public sealed partial class MainWindow
{
    private OfficialArtworkWindow? artworkWindow;

    private ReadingPosition CaptureContinuousEntryAnchor(ReadingPosition anchor)
    {
        if (anchor.Offset >= 0) return anchor with { Offset = 0 };
        var element = model.Settings.Mode == ReadingMode.Mushaf
            ? mushafView.RightPage.Elements.Concat(mushafView.LeftPage.Elements).FirstOrDefault(e => e.Verse.Id == anchor.Ayah)
            : cards.FirstOrDefault(e => e.Verse.Id == anchor.Ayah);
        if (element is null) return anchor with { Offset = 0 };
        var owner = (FrameworkElement?)element.RichText ?? element.Text;
        var viewport = model.Settings.Mode == ReadingMode.Mushaf
            ? (element.Verse.Page == mushafView.RightPage.Page ? mushafView.RightPage.ScriptureViewport : mushafView.LeftPage.ScriptureViewport)
            : scroller;
        if (!IsVisualAttached(owner, viewport)) return anchor with { Offset = 0 };
        var start = element.Link?.Inlines.OfType<Run>().FirstOrDefault()?.ContentStart
            ?? element.Text.Inlines.OfType<Run>().FirstOrDefault()?.ContentStart ?? element.Text.ContentStart;
        var origin = owner.TransformToVisual(viewport).TransformPoint(new());
        foreach (var offset in System.Globalization.StringInfo.ParseCombiningCharacters(element.Verse.Text))
        {
            if (offset > 0 && !char.IsWhiteSpace(element.Verse.Text[offset - 1])) continue;
            var pointer = start.GetPositionAtOffset(offset, LogicalDirection.Forward);
            if (pointer is null) continue;
            var rect = pointer.GetCharacterRect(LogicalDirection.Forward);
            if (rect.Height > 0 && origin.Y + rect.Y >= 0 && origin.Y + rect.Y < viewport!.ActualHeight)
                return new(anchor.Ayah, TextOffset: offset);
        }
        return anchor with { Offset = 0 };
    }

    private void UpdateContinuousChrome()
    {
        FitToggle.Visibility = Visibility.Collapsed;
        var ready = model.Ready && !model.IsBusy;
        NextButton.IsEnabled = ready && (IsPreviewOpening || continuousBook.CanNext);
        PreviousButton.IsEnabled = ready && (IsPreviewOpening ? !mushafView.IsSpread && model.Position.Ayah.Surah == 2 : continuousBook.CanPrevious);
        var spread = IsPreviewOpening ? mushafView.IsSpread : continuousBook.IsSpread;
        ToolTipService.SetToolTip(NextButton, "Next adaptive reading leaf/spread (Left); canonical page entry remains available above");
        ToolTipService.SetToolTip(PreviousButton, "Previous adaptive reading leaf/spread (Right)");
        AutomationProperties.SetName(NextButton, "Next adaptive leaves (left / forward)");
        AutomationProperties.SetName(PreviousButton, "Previous adaptive leaves (right / backward)");
        var selected = CurrentActionVerse is { } verse ? $"Selected {verse.Id} / canonical p. {verse.Page}" : IsEdgeDrawer ? "No ayah selected" : "";
        if (IsPreviewOpening)
        {
            PositionLabel.Text = spread ? "Compact opening · canonical pages 1–2" : $"Compact opening · canonical page {mushafView.RightPage.Page}";
        }
        else if (continuousBook.Ready)
        {
            var visible = continuousBook.VisibleLeaves().Where(l => l.Spans.Count > 0).ToArray();
            if (visible.Length == 0) return;
            var first = visible[0].Spans[0].Source;
            var last = visible[^1].Spans[^1].Source;
            var leafCaption = visible.Length == 1 ? $"Adaptive leaf {continuousBook.VisibleLeaf + 1}" : $"Adaptive leaves {continuousBook.VisibleLeaf + 1}–{continuousBook.VisibleLeaf + visible.Length}";
            PositionLabel.Text = $"{leafCaption} · Canonical {first.Page}–{last.Page}";
            ToolTipService.SetToolTip(PositionLabel, "Adaptive leaf identities follow one canonical stream for this layout. Font, viewport or DPI changes create a different layout; bookmarks retain stable source anchors. Basmalah presentation provenance remains under review.");
            PagePicker.Text = first.Page.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        else PositionLabel.Text = continuousBook.CompositionError ?? "Composing verified scripture… · Escape to cancel";
        // Keep the two existing footer lines bounded at narrow widths. Selected
        // commentary and visible canonical references have distinct labels.
        PositionLabel.FontSize = 12;
        PositionLabel.TextTrimming = TextTrimming.CharacterEllipsis;
        ProvisionalNotice.Text = selected;
        ToolTipService.SetToolTip(ProvisionalNotice, "King Fahd Glorious Qur’an Printing Complex · Hafs Unicode / Tafsir al-Muyassar · provisional development source");
    }

    private async void PreviewKey(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await ChangeSettingsAsync(model.Settings with { Mode = IsContinuousPreview ? ReadingMode.Mushaf : ReadingMode.AdaptiveBook });
    }

    private void CopySelectedAyah(object sender, RoutedEventArgs args)
    {
        if (CurrentActionVerse is not { } verse) return;
        CopyWholeAyah(verse);
    }

    private void OpenArtworkReference(object sender, RoutedEventArgs args)
    {
        if (artworkWindow is null)
        {
            artworkWindow = new();
            artworkWindow.Closed += (_, _) => artworkWindow = null;
        }
        artworkWindow.Activate();
    }
}
