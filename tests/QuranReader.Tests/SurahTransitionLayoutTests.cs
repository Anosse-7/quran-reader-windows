using QuranReader.Core;

namespace QuranReader.Tests;

public sealed class SurahTransitionLayoutTests
{
    [Theory]
    [InlineData(500, 300, 120, 50, true)]
    [InlineData(470, 300, 120, 50, true)]
    [InlineData(469.9, 300, 120, 50, false)]
    [InlineData(500, 400, 70, 50, false)]
    public void IntroRequiresItsWholeMeasuredFirstLine(double capacity, double top, double intro, double line, bool fits)
    {
        Assert.Equal(fits, SurahTransitionLayout.Fits(capacity, new(top, intro, line)));
    }

    [Theory]
    [InlineData(0, 0, 100, 50)]
    [InlineData(500, -1, 100, 50)]
    [InlineData(500, 0, 0, 50)]
    [InlineData(500, 0, 100, 0)]
    [InlineData(double.NaN, 0, 100, 50)]
    [InlineData(500, 0, double.PositiveInfinity, 50)]
    public void InvalidNativeGeometryFailsVisibly(double capacity, double top, double intro, double line)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SurahTransitionLayout.Fits(capacity, new(top, intro, line)));
    }

    [Theory]
    [InlineData(18)]
    [InlineData(28)]
    [InlineData(32)]
    [InlineData(56)]
    public void TitleRemainsProminentAtSupportedScales(double scale)
    {
        Assert.InRange(SurahHeaderTypography.TitleSize(scale), scale, ReadingLayout.MaxManualScale);
    }
}
