namespace QuranReader.Core;

public readonly record struct ColorRgb(byte R, byte G, byte B)
{
    public ColorRgb(int rgb) : this((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb) { }

    public double RelativeLuminance
    {
        get
        {
            static double Linear(byte c)
            {
                var v = c / 255.0;
                return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Linear(R) + 0.7152 * Linear(G) + 0.0722 * Linear(B);
        }
    }

    public static double ContrastRatio(ColorRgb c1, ColorRgb c2)
    {
        var l1 = c1.RelativeLuminance;
        var l2 = c2.RelativeLuminance;
        var lighter = Math.Max(l1, l2);
        var darker = Math.Min(l1, l2);
        return (lighter + 0.05) / (darker + 0.05);
    }
}

public sealed record ThemePaletteTokens(
    ColorRgb CanvasBackground,
    ColorRgb ReadingSurface,
    ColorRgb MushafPageSurface,
    ColorRgb TafsirSurface,
    ColorRgb ScriptureForeground,
    ColorRgb TafsirForeground,
    ColorRgb MetadataForeground,
    ColorRgb AccentColor,
    ColorRgb BorderColor,
    ColorRgb SeparatorColor,
    ColorRgb SelectedSurface,
    ColorRgb SelectedBorder,
    ColorRgb HoverSurface,
    ColorRgb ToolbarSurface,
    ColorRgb FocusIndicator
);

public static class ThemeTokens
{
    public static readonly ThemePaletteTokens Dark = new(
        CanvasBackground: new(0x171B20),
        ReadingSurface: new(0x20252B),
        MushafPageSurface: new(0x252A30),
        TafsirSurface: new(0x1D2228),
        ScriptureForeground: new(0xF0E8D8),
        TafsirForeground: new(0xE5E2DD),
        MetadataForeground: new(0xB8B5AF),
        AccentColor: new(0xC9AC78),
        BorderColor: new(0x323942),
        SeparatorColor: new(0x282F38),
        SelectedSurface: new(0x2C3440),
        SelectedBorder: new(0xC9AC78),
        HoverSurface: new(0x262D36),
        ToolbarSurface: new(0x1C2127),
        FocusIndicator: new(0xC9AC78)
    );

    public static readonly ThemePaletteTokens Light = new(
        CanvasBackground: new(0xF4F6F8),
        ReadingSurface: new(0xFFFFFF),
        MushafPageSurface: new(0xFCFCFD),
        TafsirSurface: new(0xF8F9FA),
        ScriptureForeground: new(0x1A2026),
        TafsirForeground: new(0x2B323A),
        MetadataForeground: new(0x5E6874),
        AccentColor: new(0x826430),
        BorderColor: new(0xDDE1E6),
        SeparatorColor: new(0xE8ECF0),
        SelectedSurface: new(0xEEF3F8),
        SelectedBorder: new(0x826430),
        HoverSurface: new(0xF5F8FA),
        ToolbarSurface: new(0xFFFFFF),
        FocusIndicator: new(0x826430)
    );

    public static readonly ThemePaletteTokens Sepia = new(
        CanvasBackground: new(0xEFE9DC),
        ReadingSurface: new(0xFBF6EA),
        MushafPageSurface: new(0xFAF4E6),
        TafsirSurface: new(0xF4EEE2),
        ScriptureForeground: new(0x2D251E),
        TafsirForeground: new(0x3D342A),
        MetadataForeground: new(0x736859),
        AccentColor: new(0x805A25),
        BorderColor: new(0xDCD3BF),
        SeparatorColor: new(0xE6DDCB),
        SelectedSurface: new(0xECE2CC),
        SelectedBorder: new(0x805A25),
        HoverSurface: new(0xF5ECE0),
        ToolbarSurface: new(0xFAF5E8),
        FocusIndicator: new(0x805A25)
    );

    public static ThemePaletteTokens Resolve(ReadingTheme theme, bool systemIsDark = false) => theme switch
    {
        ReadingTheme.Dark => Dark,
        ReadingTheme.Light => Light,
        ReadingTheme.Sepia => Sepia,
        ReadingTheme.System => systemIsDark ? Dark : Light,
        _ => Sepia
    };
}
