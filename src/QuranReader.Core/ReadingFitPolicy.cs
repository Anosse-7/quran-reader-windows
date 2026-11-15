namespace QuranReader.Core;

public enum ReadingFitCategory { ExactFit, SmallOverflow, Scrollable }

public sealed record ReadingFitAssessment(ReadingFitCategory Category, double OverflowDip)
{
    public bool FitsWithoutScroll => Category == ReadingFitCategory.ExactFit;
    public bool FitsWithinTolerance => Category != ReadingFitCategory.Scrollable;
}

/// <summary>
/// Describes measured native overflow without changing text scale or geometry.
/// Eight DIP is the existing scroll-affordance tolerance, not a rounding allowance
/// or a claim that all content is visible without scrolling.
/// </summary>
public static class ReadingFitPolicy
{
    public const double SmallOverflowToleranceDip = 8;

    public static ReadingFitAssessment Assess(double overflowDip)
    {
        if (!double.IsFinite(overflowDip) || overflowDip < 0)
            throw new ArgumentOutOfRangeException(nameof(overflowDip), "Measured overflow must be finite and non-negative.");

        return new(overflowDip == 0 ? ReadingFitCategory.ExactFit
            : overflowDip <= SmallOverflowToleranceDip ? ReadingFitCategory.SmallOverflow
            : ReadingFitCategory.Scrollable, overflowDip);
    }
}
