namespace QuranReader.Core;

public static class SurahHeaderTypography
{
    public static double TitleSize(double scriptureSize) => Math.Clamp(scriptureSize * 1.12, 24, ReadingLayout.MaxManualScale);
}

public readonly record struct MeasuredSurahIntro(double Top, double IntroHeight, double FirstLineHeight)
{
    public double RequiredHeight => IntroHeight + FirstLineHeight;
}

public static class SurahTransitionLayout
{
    public static bool Fits(double capacity, MeasuredSurahIntro group)
    {
        if (!double.IsFinite(capacity) || !double.IsFinite(group.Top) || !double.IsFinite(group.IntroHeight) ||
            !double.IsFinite(group.FirstLineHeight) || capacity <= 0 || group.Top < 0 || group.IntroHeight <= 0 || group.FirstLineHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(group), "A transition requires positive native geometry.");
        return group.Top + group.RequiredHeight <= capacity;
    }
}
