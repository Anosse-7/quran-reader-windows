using QuranReader.Core;

namespace QuranReader.Tests;

public sealed class ReadingFitTests
{
    [Theory]
    [InlineData(0, ReadingFitCategory.ExactFit, true, true)]
    [InlineData(0.00001, ReadingFitCategory.SmallOverflow, false, true)]
    [InlineData(3, ReadingFitCategory.SmallOverflow, false, true)]
    [InlineData(8, ReadingFitCategory.SmallOverflow, false, true)]
    [InlineData(8.00001, ReadingFitCategory.Scrollable, false, false)]
    [InlineData(94, ReadingFitCategory.Scrollable, false, false)]
    public void MeasuredOverflowDistinguishesExactFitFromAcceptedTolerance(double overflow,
        ReadingFitCategory category, bool exact, bool accepted)
    {
        var result = ReadingFitPolicy.Assess(overflow);
        Assert.Equal(category, result.Category);
        Assert.Equal(overflow, result.OverflowDip);
        Assert.Equal(exact, result.FitsWithoutScroll);
        Assert.Equal(accepted, result.FitsWithinTolerance);
    }

    [Fact]
    public void InvalidGeometryCannotBeReportedAsFitting()
    {
        foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1 })
            Assert.Throws<ArgumentOutOfRangeException>(() => ReadingFitPolicy.Assess(invalid));
    }
}
