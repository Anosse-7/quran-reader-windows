using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using QuranReader.Core;
using Windows.Foundation;

namespace QuranReader.App;

/// <summary>Original vector cartouche and unchanged headpiece, in one native flow item.</summary>
internal sealed class SurahHeader : StackPanel
{
    private readonly Grid banner = new() { Margin = new(0, 4, 0, 4) };
    private readonly Canvas ornament = new() { IsHitTestVisible = false };
    private readonly TextBlock title;
    private readonly TextBlock? basmalah;
    private ReaderPalette palette;
    public int Surah { get; }
    public double TitleSize => title.FontSize;
    public double BannerHeight => banner.ActualHeight;
    public double BasmalahHeight => basmalah?.ActualHeight ?? 0;
    public string AccessibleTitle => title.Text;

    public SurahHeader(Verse first, double scale, double width, ReaderPalette colors)
    {
        Surah = first.Id.Surah;
        palette = colors;
        Width = width;
        FlowDirection = FlowDirection.RightToLeft;
        title = new()
        {
            Text = $"سُورَةُ {first.SurahName}",
            FontFamily = AyahElements.Hafs,
            FontSize = SurahHeaderTypography.TitleSize(scale),
            LineHeight = SurahHeaderTypography.TitleSize(scale) * ReadingLayout.MushafLineHeightRatio,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new(48, 6, 48, 6),
            Language = "ar-SA"
        };
        AutomationProperties.SetName(this, title.Text);
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level2);
        AutomationProperties.SetAccessibilityView(ornament, AccessibilityView.Raw);
        banner.Children.Add(ornament);
        banner.Children.Add(title);
        NativeTextProviderLifetime.Protect(title);
        Children.Add(banner);
        if (SurahMetadata.HasBasmalahHeader(Surah))
        {
            // Provisional existing policy, preserved exactly. No source derivation.
            basmalah = new()
            {
                Text = SurahMetadata.CanonicalBasmalah,
                FontFamily = AyahElements.Hafs,
                FontSize = scale,
                LineHeight = scale * ReadingLayout.MushafLineHeightRatio,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                TextAlignment = TextAlignment.Center,
                Language = "ar-SA"
            };
            Children.Add(basmalah);
            NativeTextProviderLifetime.Protect(basmalah);
        }
        banner.SizeChanged += (_, _) => DrawOrnament();
        ApplyPalette(colors);
    }

    public void ApplyPalette(ReaderPalette colors)
    {
        palette = colors;
        title.Foreground = colors.ScriptureForeground;
        if (basmalah is not null) basmalah.Foreground = colors.ScriptureForeground;
        foreach (var path in ornament.Children.OfType<Microsoft.UI.Xaml.Shapes.Path>()) path.Stroke = colors.AccentColor;
    }

    public double MeasureFirstLine(string source, double scale)
    {
        var line = new TextBlock
        {
            Text = source,
            FontFamily = AyahElements.Hafs,
            FontSize = scale,
            LineHeight = scale * ReadingLayout.MushafLineHeightRatio,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 1,
            FlowDirection = FlowDirection.RightToLeft,
            Language = "ar-SA"
        };
        line.Measure(new(Width, double.PositiveInfinity));
        return line.DesiredSize.Height;
    }

    private void DrawOrnament()
    {
        var width = banner.ActualWidth;
        var height = banner.ActualHeight;
        if (width <= 0 || height <= 0) return;
        ornament.Children.Clear();
        // Original symmetrical scalloped cartouche, authored as native Bezier paths.
        // Coordinates scale the ornament only. Native Arabic is never transformed.
        foreach (var inset in new[] { 0.0, 3.5 })
        {
            Point P(double x, double y) => new(8 + inset + x * (width - 16 - 2 * inset), 2 + inset + y * (height - 4 - 2 * inset));
            var figure = new PathFigure { StartPoint = P(0, .5), IsClosed = true, IsFilled = false };
            void Curve(double x1, double y1, double x2, double y2, double x3, double y3) =>
                figure.Segments.Add(new BezierSegment { Point1 = P(x1, y1), Point2 = P(x2, y2), Point3 = P(x3, y3) });
            Curve(.065, .5, .035, .1, .14, .1);
            Curve(.32, .1, .43, .14, .5, .015);
            Curve(.57, .14, .68, .1, .86, .1);
            Curve(.965, .1, .935, .5, 1, .5);
            Curve(.935, .5, .965, .9, .86, .9);
            Curve(.68, .9, .57, .86, .5, .985);
            Curve(.43, .86, .32, .9, .14, .9);
            Curve(.035, .9, .065, .5, 0, .5);
            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            ornament.Children.Add(new Microsoft.UI.Xaml.Shapes.Path { Data = geometry, Stroke = palette.AccentColor, StrokeThickness = inset == 0 ? 1.2 : .65 });
        }
        foreach (var center in new[] { 29.0, width - 29 })
        {
            var figure = new PathFigure { StartPoint = new(center, height / 2 - 8), IsClosed = true, IsFilled = false };
            figure.Segments.Add(new BezierSegment { Point1 = new(center + 2, height / 2 - 2), Point2 = new(center + 8, height / 2 - 2), Point3 = new(center + 9, height / 2) });
            figure.Segments.Add(new BezierSegment { Point1 = new(center + 3, height / 2 + 2), Point2 = new(center + 2, height / 2 + 7), Point3 = new(center, height / 2 + 8) });
            figure.Segments.Add(new BezierSegment { Point1 = new(center - 2, height / 2 + 2), Point2 = new(center - 8, height / 2 + 2), Point3 = new(center - 9, height / 2) });
            figure.Segments.Add(new BezierSegment { Point1 = new(center - 3, height / 2 - 2), Point2 = new(center - 2, height / 2 - 7), Point3 = new(center, height / 2 - 8) });
            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            ornament.Children.Add(new Microsoft.UI.Xaml.Shapes.Path { Data = geometry, Stroke = palette.AccentColor, StrokeThickness = 1 });
        }
    }
}
