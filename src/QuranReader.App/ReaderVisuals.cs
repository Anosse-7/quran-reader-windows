using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using QuranReader.Core;
using Windows.UI;

namespace QuranReader.App;

internal sealed class ReaderPalette
{
    [ThreadStatic]
    private static Dictionary<ThemePaletteTokens, ReaderPalette>? threadPalettes;
    public ThemePaletteTokens Tokens { get; }
    public SolidColorBrush CanvasBackground { get; }
    public SolidColorBrush ReadingSurface { get; }
    public SolidColorBrush MushafPageSurface { get; }
    public SolidColorBrush TafsirSurface { get; }
    public SolidColorBrush ScriptureForeground { get; }
    public SolidColorBrush TafsirForeground { get; }
    public SolidColorBrush MetadataForeground { get; }
    public SolidColorBrush AccentColor { get; }
    public SolidColorBrush BorderColor { get; }
    public SolidColorBrush SeparatorColor { get; }
    public SolidColorBrush SelectedSurface { get; }
    public SolidColorBrush SelectedBorder { get; }
    public SolidColorBrush HoverSurface { get; }
    public SolidColorBrush ToolbarSurface { get; }
    public SolidColorBrush FocusIndicator { get; }
    public SolidColorBrush ScrimBackground { get; }
    public SolidColorBrush PopupSurface { get; }
    public SolidColorBrush ScriptureBoxSurface { get; }
    public SolidColorBrush TransparentSurface { get; } = new(Colors.Transparent);

    public ReaderPalette(ThemePaletteTokens tokens)
    {
        Tokens = tokens;
        CanvasBackground = Brush(tokens.CanvasBackground);
        ReadingSurface = Brush(tokens.ReadingSurface);
        MushafPageSurface = Brush(tokens.MushafPageSurface);
        TafsirSurface = Brush(tokens.TafsirSurface);
        ScriptureForeground = Brush(tokens.ScriptureForeground);
        TafsirForeground = Brush(tokens.TafsirForeground);
        MetadataForeground = Brush(tokens.MetadataForeground);
        AccentColor = Brush(tokens.AccentColor);
        BorderColor = Brush(tokens.BorderColor);
        SeparatorColor = Brush(tokens.SeparatorColor);
        SelectedSurface = Brush(tokens.SelectedSurface);
        SelectedBorder = Brush(tokens.SelectedBorder);
        HoverSurface = Brush(tokens.HoverSurface);
        ToolbarSurface = Brush(tokens.ToolbarSurface);
        FocusIndicator = Brush(tokens.FocusIndicator);

        var isDark = tokens.CanvasBackground.RelativeLuminance < 0.2;
        var isSepia = Math.Abs(tokens.CanvasBackground.R - 0xEF) < 15 && Math.Abs(tokens.CanvasBackground.G - 0xE9) < 15;
        ScrimBackground = isDark
            ? new SolidColorBrush(Color.FromArgb(175, 12, 15, 18))
            : isSepia
                ? new SolidColorBrush(Color.FromArgb(135, 45, 36, 26))
                : new SolidColorBrush(Color.FromArgb(125, 20, 24, 30));
        PopupSurface = Brush(tokens.ReadingSurface);
        ScriptureBoxSurface = isDark
            ? new SolidColorBrush(Color.FromArgb(255, 0x1E, 0x23, 0x29))
            : isSepia
                ? new SolidColorBrush(Color.FromArgb(255, 0xF4, 0xEE, 0xE0))
                : new SolidColorBrush(Color.FromArgb(255, 0xF6, 0xF8, 0xFA));
    }

    public static ReaderPalette For(ReadingTheme theme, ElementTheme actualSystemTheme = ElementTheme.Light)
    {
        var isDark = actualSystemTheme == ElementTheme.Dark;
        var tokens = ThemeTokens.Resolve(theme, isDark);
        // Brushes are UI-thread resources. Reuse the three immutable theme sets
        // on that thread instead of allocating WinRT brushes on every state tick.
        threadPalettes ??= [];
        if (!threadPalettes.TryGetValue(tokens, out var palette))
            threadPalettes[tokens] = palette = new(tokens);
        return palette;
    }

    public static Color ToColor(ColorRgb rgb) => Color.FromArgb(255, rgb.R, rgb.G, rgb.B);
    public static SolidColorBrush Brush(ColorRgb rgb) => new(ToColor(rgb));
    public static SolidColorBrush Brush(int rgb) => new(Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
}

internal sealed class AyahElement
{
    public Verse Verse { get; }

    // Continuous Mushaf Mode presentation properties
    public Hyperlink? Link { get; set; }
    public RichTextBlock? RichText { get; set; }
    public int StartIndex { get; set; }
    public int Length { get; set; }

    // Tafsir Workspace Mode card presentation properties
    public Grid Card { get; set; }
    public TextBlock Text { get; set; }
    public TextBlock Label { get; set; }

    public AyahElement(Verse verse)
    {
        Verse = verse;
        Card = new Grid();
        Text = new TextBlock();
        Label = new TextBlock();
    }

    public AyahElement(Verse verse, Grid card, TextBlock text, TextBlock label)
    {
        Verse = verse;
        Card = card;
        Text = text;
        Label = label;
    }
}

internal static class AyahElements
{
    public static readonly FontFamily Hafs = new("ms-appx:///Assets/kfgqpc_hafs_v30.ttf#kfgqpc_hafs_v3");

    public static AyahElement Create(Verse verse)
    {
        var card = new Grid
        {
            Padding = new Thickness(20, 10, 20, 12),
            Tag = verse,
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 760
        };
        card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var label = new TextBlock
        {
            Text = $"سُورَةُ {verse.SurahName} · آية {SurahMetadata.ToEasternArabicDigits(verse.Id.Ayah)}",
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.Medium,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Thickness(0, 0, 0, 6),
            Opacity = 0.85
        };

        var text = new TextBlock
        {
            Text = verse.Text,
            FontFamily = Hafs,
            FlowDirection = FlowDirection.RightToLeft,
            Language = "ar-SA",
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Start,
            IsTextSelectionEnabled = true,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight
        };
        Grid.SetRow(text, 1);

        card.Children.Add(label);
        card.Children.Add(text);

        AutomationProperties.SetName(card, $"Ayah {verse.Id}");
        ToolTipService.SetToolTip(card, $"Ayah {verse.Id} · Select for Tafsir al-Muyassar");
        return new(verse, card, text, label);
    }

    public static void Style(AyahElement element, ReaderSettings settings, AyahId? selected, ReaderPalette palette)
    {
        if (element.Text is not null)
        {
            element.Text.FontSize = settings.Scale;
            element.Text.LineHeight = settings.Scale * ReadingLayout.TafsirLineHeightRatio;
            element.Text.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            element.Text.Foreground = palette.ScriptureForeground;
        }

        if (element.Label is not null)
        {
            element.Label.Foreground = palette.MetadataForeground;
        }

        if (element.Card is not null)
        {
            var isSelected = element.Verse.Id == selected;
            element.Card.Background = isSelected ? palette.SelectedSurface : palette.TransparentSurface;
            element.Card.BorderBrush = isSelected ? palette.AccentColor : palette.TransparentSurface;
            element.Card.BorderThickness = isSelected ? new Thickness(1.5) : new Thickness(0);
        }
    }

    public static Border CreateUnwanHeader(int surahNo, string surahName, int totalAyat, ReaderPalette palette, bool isCompact = false)
    {
        var border = new Border
        {
            Height = isCompact ? 38 : 50,
            Margin = isCompact ? new Thickness(0, 4, 0, 6) : new Thickness(0, 8, 0, 10),
            Padding = isCompact ? new Thickness(12, 2, 12, 2) : new Thickness(16, 4, 16, 4),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1.5),
            BorderBrush = palette.AccentColor,
            Background = palette.ToolbarSurface,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var grid = new Grid
        {
            FlowDirection = FlowDirection.RightToLeft
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var classificationText = new TextBlock
        {
            Text = SurahMetadata.RevelationTypeArabic(surahNo),
            FontSize = isCompact ? 11 : 12,
            Foreground = palette.MetadataForeground,
            FlowDirection = FlowDirection.RightToLeft,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var titleText = new TextBlock
        {
            Text = $"سُورَةُ {surahName}",
            FontFamily = Hafs,
            FontSize = isCompact ? 17 : 20,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = palette.AccentColor,
            FlowDirection = FlowDirection.RightToLeft,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var countText = new TextBlock
        {
            Text = $"{SurahMetadata.ToEasternArabicDigits(totalAyat)} آيَاتٍ",
            FontSize = isCompact ? 11 : 12,
            Foreground = palette.MetadataForeground,
            FlowDirection = FlowDirection.RightToLeft,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        grid.Children.Add(classificationText);
        grid.Children.Add(titleText);
        grid.Children.Add(countText);

        Grid.SetColumn(classificationText, 0);
        Grid.SetColumn(titleText, 1);
        Grid.SetColumn(countText, 2);

        border.Child = grid;
        return border;
    }

    public static TextBlock CreateBasmalah(ReaderPalette palette, double scale, bool isCompact = false)
    {
        var fontSize = isCompact ? Math.Max(18, scale * 0.90) : Math.Max(20, scale * 0.95);
        return new TextBlock
        {
            Text = SurahMetadata.CanonicalBasmalah,
            FontFamily = Hafs,
            FontSize = fontSize,
            LineHeight = fontSize * ReadingLayout.MushafLineHeightRatio,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            Foreground = palette.ScriptureForeground,
            FlowDirection = FlowDirection.RightToLeft,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = isCompact ? new Thickness(0, 2, 0, 6) : new Thickness(0, 6, 0, 10),
            Language = "ar-SA"
        };
    }

    public static void RefineList(ListView list)
    {
        foreach (var key in new[]
        {
            "ListViewItemBackgroundSelected",
            "ListViewItemBackgroundSelectedPointerOver",
            "ListViewItemBackgroundSelectedPressed",
            "ListViewItemBackgroundPointerOver"
        })
        {
            list.Resources[key] = new SolidColorBrush(Colors.Transparent);
        }

        var style = new Style(typeof(ListViewItem));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        list.ItemContainerStyle = style;
    }
}
