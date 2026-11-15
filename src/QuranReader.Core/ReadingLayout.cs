namespace QuranReader.Core;

public static class ReadingLayout
{
    public const int TransitionMilliseconds = 220;
    public const double MushafLineHeightRatio = 1.80;
    public const double OpeningSpreadLineHeightRatio = 1.90;
    public const double TafsirLineHeightRatio = 1.75;
    public const double CommentaryLineHeightRatio = 1.65;
    public const double MinComfortableScale = 18.0;
    public const double MaxManualScale = 56.0;
    public const double MaxFitScale = 28.0;

    public static double SpreadThreshold(double scale) => Math.Max(1140, 2 * (scale * 14 + 88) + 24);
    public static double SinglePageThreshold(double scale) => Math.Max(460, scale * 14 + 90);

    public static bool ShowSpread(double availableWidth, double scale) => availableWidth >= SpreadThreshold(scale);
    public static int SpreadStart(int page) => page % 2 == 0 ? page - 1 : page;
    public static int NextPage(int page, bool spread) => Math.Min(604, spread ? SpreadStart(page) + 2 : page + 1);
    public static int PreviousPage(int page, bool spread) => Math.Max(1, spread ? SpreadStart(page) - 2 : page - 1);
    public static int MotionDuration(bool systemAnimationsEnabled, bool reduceMotion) => systemAnimationsEnabled && !reduceMotion ? TransitionMilliseconds : 0;

    public record PageLayoutEstimate(double Scale, double EstimatedHeight, bool FitsWithoutScroll);

    /// <summary>
    /// Counts base Arabic consonant/letter glyphs excluding combining diacritics (tashkeel, waqf marks, dagger alifs).
    /// In Qur'anic Unicode, combining marks (category NonSpacingMark) do not advance horizontal text layout.
    /// </summary>
    public static int CountBaseCharacters(string text)
    {
        var count = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (char.GetUnicodeCategory(text[i]) != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>
    /// Content-aware layout estimation for a Mushaf page based on actual base character count,
    /// surah headpieces, and Basmalah blocks after Arabic shaping.
    /// </summary>
    public static PageLayoutEstimate EstimatePageLayout(
        IReadOnlyList<Verse> verses,
        double availableWidth,
        double availableHeight,
        bool isMultiSurahSpread = false)
    {
        if (verses.Count == 0) return new(24.0, 0, true);

        var pageNo = verses[0].Page;
        var distinctSurahs = verses.Select(v => v.Id.Surah).Distinct().ToList();
        var surahCount = distinctSurahs.Count;
        var isMultiSurah = surahCount > 1 || isMultiSurahSpread || pageNo >= 587;

        // Count new surah headers and Basmalah blocks
        var headerCount = verses.Count(v => v.Id.Ayah == 1);
        var basmalahCount = verses.Count(v => v.Id.Ayah == 1 && SurahMetadata.HasBasmalahHeader(v.Id.Surah));

        // Fixed page chrome
        // Outer frame: 12 DIP, Header rule: 40 DIP, Footer pill: 36 DIP
        // Frame padding: 20 DIP for multi-surah, 32 DIP for standard
        var framePadding = isMultiSurah ? 20.0 : 32.0;
        var fixedChrome = 12.0 + 40.0 + 36.0 + framePadding;

        // Total base characters excluding combining diacritics (which do not consume horizontal line advance)
        var totalBaseChars = verses.Sum(v => CountBaseCharacters(v.Text) + 2);

        // Effective column width available for text inside frame padding
        var textColumnWidth = Math.Max(260.0, availableWidth - (isMultiSurah ? 48.0 : 56.0));

        // Multi-surah pages with dense banners maintain proportional balance between text and headpieces (max 24.0 DIP)
        var maxCandidate = isMultiSurah ? 24.0 : MaxFitScale;

        // Evaluate candidate scales from maxCandidate down to MinComfortableScale in 0.5 DIP steps
        for (var s = maxCandidate; s >= MinComfortableScale; s -= 0.5)
        {
            var est = CalculateHeightAtScale(s, headerCount, basmalahCount, totalBaseChars, textColumnWidth, fixedChrome, isMultiSurah);
            if (est <= availableHeight)
            {
                return new(s, est, true);
            }
        }

        // When content cannot fit without dropping below MinComfortableScale,
        // preserve reading comfort and truthfully report that scrolling is required.
        var minScaleHeight = CalculateHeightAtScale(MinComfortableScale, headerCount, basmalahCount, totalBaseChars, textColumnWidth, fixedChrome, isMultiSurah);
        return new(MinComfortableScale, minScaleHeight, minScaleHeight <= availableHeight);
    }

    private static double CalculateHeightAtScale(
        double scale,
        int headerCount,
        int basmalahCount,
        int totalBaseChars,
        double textColumnWidth,
        double fixedChrome,
        bool isMultiSurah)
    {
        var headerHeight = isMultiSurah ? 46.0 : 64.0;
        var basmalahHeight = isMultiSurah ? (scale * 0.9 * MushafLineHeightRatio + 8.0) : (scale * MushafLineHeightRatio + 16.0);

        var nonTextHeight = fixedChrome + (headerCount * headerHeight) + (basmalahCount * basmalahHeight);

        // In Hafs Arabic typography with OpenType ligatures, average base glyph advance is approximately 0.40 * scale
        var charsPerLine = Math.Max(15.0, textColumnWidth / (0.40 * scale));
        var wrappedLines = Math.Ceiling(totalBaseChars / charsPerLine);
        var lineHeight = scale * MushafLineHeightRatio;
        var textHeight = wrappedLines * lineHeight;

        return nonTextHeight + textHeight;
    }

    /// <summary>
    /// Content-aware scale for facing pages in a spread, ensuring consistent typography across both pages.
    /// </summary>
    public static (double Scale, bool FitsWithoutScroll) CalculateSpreadScale(
        IReadOnlyList<Verse> rightPage,
        IReadOnlyList<Verse> leftPage,
        double availableWidthPerPage,
        double availableHeight)
    {
        var isMulti = rightPage.Select(v => v.Id.Surah).Distinct().Count() > 1 ||
                      leftPage.Select(v => v.Id.Surah).Distinct().Count() > 1 ||
                      (rightPage.Count > 0 && rightPage[0].Page >= 587) ||
                      (leftPage.Count > 0 && leftPage[0].Page >= 587);

        var rightEst = EstimatePageLayout(rightPage, availableWidthPerPage, availableHeight, isMultiSurahSpread: isMulti);
        var leftEst = leftPage.Count > 0
            ? EstimatePageLayout(leftPage, availableWidthPerPage, availableHeight, isMultiSurahSpread: isMulti)
            : rightEst;

        // Typography consistency across facing pages: use the smaller scale of the two pages
        var spreadScale = Math.Min(rightEst.Scale, leftEst.Scale);
        var bothFit = rightEst.FitsWithoutScroll && leftEst.FitsWithoutScroll;

        return (spreadScale, bothFit);
    }

    public static double CalculateFitScale(double availableHeight)
    {
        var contentHeight = Math.Max(200, availableHeight - 110);
        var fitScale = contentHeight / (15.5 * MushafLineHeightRatio);
        return Math.Clamp(Math.Round(fitScale, 1), 20.0, MaxFitScale);
    }

    public static double EffectiveMushafScale(ReaderSettings settings, double availableHeight)
    {
        if (settings.FitSpread && availableHeight > 400)
        {
            return CalculateFitScale(availableHeight);
        }
        return settings.Scale;
    }

    public static double EffectiveMushafScale(
        ReaderSettings settings,
        IReadOnlyList<Verse> rightPage,
        IReadOnlyList<Verse> leftPage,
        double availableWidthPerPage,
        double availableHeight)
    {
        if (settings.FitSpread && availableHeight > 400)
        {
            var (scale, _) = CalculateSpreadScale(rightPage, leftPage, availableWidthPerPage, availableHeight);
            return scale;
        }
        return settings.Scale;
    }
}

/// <summary>
/// Explicit, testable layout policy for the exceptional opening pages (Pages 1 & 2: Al-Fatihah and Al-Baqarah 1-5).
/// Preserves the compact, illuminated cartouche presentation without generic 15-line reflow or arbitrary vertical centering.
/// </summary>
public static class OpeningSpreadLayoutPolicy
{
    public const double OpeningLineHeightRatio = 1.90;
    public const double MaxOpeningScale = 28.0;
    public const double MinOpeningScale = 20.0;
    public const double MaxScriptureWidth = 520.0;

    public static bool IsOpeningPage(int page) => page is 1 or 2;
    public static bool IsOpeningSpread(int page1, int page2) => (page1 is 1 or 2) && (page2 is 1 or 2 || page2 == 0);

    public static (double TopPadding, double BottomPadding) CalculateOpeningPadding(double availableHeight, double contentHeight)
    {
        var rawHeadroom = Math.Max(0.0, availableHeight - contentHeight);
        if (rawHeadroom <= 16.0) return (0.0, 0.0);

        // Balanced top margin that leaves the opening cartouche sitting gracefully in upper-middle region
        // Non-negative coordinate guarantee under all viewports
        var pad = Math.Max(24.0, Math.Min(64.0, rawHeadroom * 0.30));
        return (pad, pad);
    }
}

/// <summary>
/// Layout policy for standard narrative Mushaf pages (Pages 3-604).
/// Ensures continuous reading flow and natural vertical density, eliminating unnecessary page voids.
/// </summary>
public static class StandardPageLayoutPolicy
{
    public static (double TopPadding, double BottomPadding) CalculateStandardPadding(double availableHeight, double contentHeight)
    {
        var rawHeadroom = Math.Max(0.0, availableHeight - contentHeight);
        if (rawHeadroom <= 16.0) return (0.0, 0.0);

        // Balanced vertical distribution: 35% top, 35% bottom (leaves 30% natural breathing room)
        var pad = Math.Min(32.0, rawHeadroom * 0.35);
        return (pad, pad);
    }
}
