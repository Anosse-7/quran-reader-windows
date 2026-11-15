using Microsoft.Data.Sqlite;
using QuranReader.Core;
using QuranReader.Data;

namespace QuranReader.Tests;

public sealed class Phase12FTests
{
    private static string ContentPath => Path.Combine(AppContext.BaseDirectory, "Content", "quran_reader_candidate.sqlite3");

    // ==========================================
    // MRG-001: Special Opening Page Composition
    // ==========================================

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(42, false)]
    [InlineData(604, false)]
    public void MRG001_OpeningPages_IdentifiedExplicitly(int page, bool expectedIsOpening)
    {
        Assert.Equal(expectedIsOpening, OpeningSpreadLayoutPolicy.IsOpeningPage(page));
    }

    [Fact]
    public void MRG001_OpeningSpread_SymmetricPairing_Identified()
    {
        Assert.True(OpeningSpreadLayoutPolicy.IsOpeningSpread(1, 2));
        Assert.True(OpeningSpreadLayoutPolicy.IsOpeningSpread(2, 1));
        Assert.False(OpeningSpreadLayoutPolicy.IsOpeningSpread(3, 4));
        Assert.False(OpeningSpreadLayoutPolicy.IsOpeningSpread(41, 42));
    }

    [Theory]
    [InlineData(800.0, 320.0, 64.0)] // Generous headroom (480 DIP) caps at 64 DIP
    [InlineData(600.0, 350.0, 64.0)] // Headroom 250 DIP: 250 * 0.30 = 75 -> clamped to 64
    [InlineData(400.0, 350.0, 24.0)] // Small headroom (50 DIP) uses min opening margin (24 DIP)
    [InlineData(350.0, 340.0, 0.0)]  // Very tight headroom (<= 16 DIP) yields zero padding
    public void MRG001_OpeningPadding_CalculatedTopSafeAndBalanced(double availH, double contentH, double expectedPad)
    {
        var (topPad, bottomPad) = OpeningSpreadLayoutPolicy.CalculateOpeningPadding(availH, contentH);
        Assert.Equal(expectedPad, topPad);
        Assert.Equal(expectedPad, bottomPad);
        Assert.True(topPad >= 0.0, "Opening top padding must never be negative");
    }

    [Fact]
    public void MRG001_OpeningSpread_BasmalahAndUnwanDistinction_Verified()
    {
        using var conn = new SqliteConnection($"Data Source={ContentPath};Mode=ReadOnly;Pooling=False");
        conn.Open();

        // Page 1: Surah Al-Fatihah, 7 verses. Basmalah is numbered Ayah 1 (1:1).
        using var cmd1 = conn.CreateCommand();
        cmd1.CommandText = "SELECT count(*), min(aya_no), max(aya_no) FROM reader_verses WHERE page=1";
        using var r1 = cmd1.ExecuteReader();
        Assert.True(r1.Read());
        Assert.Equal(7, r1.GetInt32(0));
        Assert.Equal(1, r1.GetInt32(1));
        Assert.Equal(7, r1.GetInt32(2));
        Assert.False(SurahMetadata.HasBasmalahHeader(1), "Surah 1 must NOT have duplicate unnumbered Basmalah header");

        // Page 2: Surah Al-Baqarah, 5 verses (ayat 1-5). Basmalah is an unnumbered headpiece.
        using var cmd2 = conn.CreateCommand();
        cmd2.CommandText = "SELECT count(*), min(aya_no), max(aya_no) FROM reader_verses WHERE page=2";
        using var r2 = cmd2.ExecuteReader();
        Assert.True(r2.Read());
        Assert.Equal(5, r2.GetInt32(0));
        Assert.Equal(1, r2.GetInt32(1));
        Assert.Equal(5, r2.GetInt32(2));
        Assert.True(SurahMetadata.HasBasmalahHeader(2), "Surah 2 must have unnumbered Basmalah headpiece preceding Ayah 1");
    }

    // ==========================================
    // MRG-002: Continuous Scripture Flow & Surah Transitions
    // ==========================================

    [Theory]
    [InlineData(750.0, 650.0, 32.0)] // 100 DIP headroom -> 32 DIP padding
    [InlineData(650.0, 600.0, 17.5)] // 50 DIP headroom -> 50 * 0.35 = 17.5 DIP padding
    [InlineData(600.0, 590.0, 0.0)]  // <= 16 DIP headroom -> 0 DIP
    public void MRG002_StandardPadding_BalancesHeadroomGracefully(double availH, double contentH, double expectedPad)
    {
        var (topPad, bottomPad) = StandardPageLayoutPolicy.CalculateStandardPadding(availH, contentH);
        Assert.Equal(expectedPad, topPad);
        Assert.Equal(expectedPad, bottomPad);
        Assert.True(topPad >= 0.0, "Standard page padding must never be negative");
    }

    [Fact]
    public void MRG002_MultiSurahPage604_ExactThreeSurahsWithCanonicalHeadings()
    {
        using var conn = new SqliteConnection($"Data Source={ContentPath};Mode=ReadOnly;Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT sura_no, min(aya_no), max(aya_no), count(*) FROM reader_verses WHERE page=604 GROUP BY sura_no ORDER BY sura_no";
        using var reader = cmd.ExecuteReader();

        var surahs = new List<(int Surah, int MinAyah, int MaxAyah, int Count)>();
        while (reader.Read())
        {
            surahs.Add((reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3)));
        }

        Assert.Equal(3, surahs.Count);
        // Surah 112 (Al-Ikhlas): 4 verses
        Assert.Equal(112, surahs[0].Surah);
        Assert.Equal(1, surahs[0].MinAyah);
        Assert.Equal(4, surahs[0].MaxAyah);
        Assert.True(SurahMetadata.HasBasmalahHeader(112));

        // Surah 113 (Al-Falaq): 5 verses
        Assert.Equal(113, surahs[1].Surah);
        Assert.Equal(1, surahs[1].MinAyah);
        Assert.Equal(5, surahs[1].MaxAyah);
        Assert.True(SurahMetadata.HasBasmalahHeader(113));

        // Surah 114 (An-Nas): 6 verses
        Assert.Equal(114, surahs[2].Surah);
        Assert.Equal(1, surahs[2].MinAyah);
        Assert.Equal(6, surahs[2].MaxAyah);
        Assert.True(SurahMetadata.HasBasmalahHeader(114));
    }

    [Fact]
    public void MRG002_SurahAtTawbah_Page187_NoBasmalah()
    {
        using var conn = new SqliteConnection($"Data Source={ContentPath};Mode=ReadOnly;Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT sura_no, min(aya_no) FROM reader_verses WHERE page=187 GROUP BY sura_no";
        using var reader = cmd.ExecuteReader();
        Assert.True(reader.Read());
        var surahNo = reader.GetInt32(0);
        var minAyah = reader.GetInt32(1);

        Assert.Equal(9, surahNo);
        Assert.Equal(1, minAyah);
        Assert.False(SurahMetadata.HasBasmalahHeader(9), "Surah At-Tawbah (9:1) canonically omits Basmalah");
    }

    // ==========================================
    // MRG-003: Keyboard Navigation with Arrow Keys
    // ==========================================

    [Fact]
    public void MRG003_SpreadProgression_RTLConventions_NextAndPrevious()
    {
        // Spread progression advances facing page pairs strictly without repeating:
        // Left arrow = Next spread (advances from 1-2 to 3-4)
        Assert.Equal(3, ReadingLayout.NextPage(1, spread: true));
        Assert.Equal(3, ReadingLayout.NextPage(2, spread: true));

        // From 3-4, advancing goes to 5-6
        Assert.Equal(5, ReadingLayout.NextPage(3, spread: true));
        Assert.Equal(5, ReadingLayout.NextPage(4, spread: true));

        // Right arrow = Previous spread (returns from 3-4 to 1-2)
        Assert.Equal(1, ReadingLayout.PreviousPage(3, spread: true));
        Assert.Equal(1, ReadingLayout.PreviousPage(4, spread: true));

        // Boundary clamping at page 1
        Assert.Equal(1, ReadingLayout.PreviousPage(1, spread: true));
        Assert.Equal(1, ReadingLayout.PreviousPage(2, spread: true));

        // Boundary clamping at page 604
        Assert.Equal(604, ReadingLayout.NextPage(603, spread: true));
        Assert.Equal(604, ReadingLayout.NextPage(604, spread: true));
    }

    [Fact]
    public void MRG003_SinglePage_Progression_RTLConventions()
    {
        // Single page mode progression:
        Assert.Equal(2, ReadingLayout.NextPage(1, spread: false));
        Assert.Equal(3, ReadingLayout.NextPage(2, spread: false));
        Assert.Equal(1, ReadingLayout.PreviousPage(2, spread: false));
        Assert.Equal(1, ReadingLayout.PreviousPage(1, spread: false));
        Assert.Equal(604, ReadingLayout.NextPage(604, spread: false));
    }

    // ==========================================
    // MRG-004: Responsive Arabic Typography
    // ==========================================

    [Fact]
    public void MRG004_LineHeightRatios_PreventDiacriticClipping()
    {
        // Standard Mushaf ratio must be >= 1.80
        Assert.True(ReadingLayout.MushafLineHeightRatio >= 1.80, "Mushaf line height ratio must be >= 1.80 to ensure diacritic clearance");
        // Opening spread ratio must be >= 1.80
        Assert.True(OpeningSpreadLayoutPolicy.OpeningLineHeightRatio >= 1.80, "Opening spread line height ratio must be >= 1.80");
        // Tafsir commentary ratio must be >= 1.70
        Assert.True(ReadingLayout.TafsirLineHeightRatio >= 1.70, "Tafsir line height ratio must be >= 1.70");
    }

    [Fact]
    public async Task MRG004_SpreadScale_Convergence_PreservesAnchor()
    {
        var repo = new ContentRepository(ContentPath);
        await repo.ValidateAsync();

        var page41 = await repo.GetPageAsync(41);
        var page42 = await repo.GetPageAsync(42);

        // At 800 DIP height, spread scale must converge comfortably between 24 and 28 DIP
        var (scale, fits) = ReadingLayout.CalculateSpreadScale(page41, page42, availableWidthPerPage: 560, availableHeight: 800);
        Assert.True(fits, "Spread for pages 41-42 must fit within 800 DIP viewport");
        Assert.InRange(scale, 24.0, 28.0);
    }
}
