using System.Net;
using Microsoft.Data.Sqlite;
using QuranReader.Core;
using QuranReader.Data;

namespace QuranReader.Tests;

public sealed class Phase12ETests
{
    private static string ContentPath => Path.Combine(AppContext.BaseDirectory, "Content", "quran_reader_candidate.sqlite3");

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 7)]
    [InlineData(2, 1)]
    [InlineData(2, 37)]
    [InlineData(2, 255)]
    public void Tafsir_UnicodeIntegrity_LogicalTextMatchesSourceExactly(int surah, int ayah)
    {
        using var conn = new SqliteConnection($"Data Source={ContentPath};Mode=ReadOnly;Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT tafsir_markup FROM reader_tafsir WHERE sura_no=$s AND aya_no=$a";
        cmd.Parameters.AddWithValue("$s", surah);
        cmd.Parameters.AddWithValue("$a", ayah);

        var rawMarkup = (string)cmd.ExecuteScalar()!;
        Assert.NotNull(rawMarkup);

        var spans = TafsirFormatter.Parse(rawMarkup);
        Assert.NotEmpty(spans);

        // Concatenated logical text of all parsed spans MUST equal the HTML-decoded raw markup exactly
        var expectedDecoded = WebUtility.HtmlDecode(rawMarkup);
        // Note: TafsirFormatter removes the <span class='aya'> and </span> tags when building runs,
        // so the concatenated span text must match the raw markup stripped of allowed tags.
        var expectedText = System.Text.RegularExpressions.Regex.Replace(
            rawMarkup,
            @"</?span(?:\s+class=['""]aya['""])?>",
            string.Empty);
        expectedText = WebUtility.HtmlDecode(expectedText);

        var actualText = string.Concat(spans.Select(s => s.Text));
        Assert.Equal(expectedText, actualText);

        // For verses with Quranic quotations (1:1, 1:7, 2:1, 2:37), verify spans include U+FD61 and U+FD60 intact
        if (rawMarkup.Contains("class='aya'"))
        {
            var ayahSpans = spans.Where(s => s.IsAyah).ToList();
            Assert.NotEmpty(ayahSpans);
            foreach (var aSpan in ayahSpans)
            {
                Assert.StartsWith("\ufd61", aSpan.Text);
                Assert.EndsWith("\ufd60", aSpan.Text);
            }
        }
    }

    [Fact]
    public void Tafsir_AllSpansAcrossEntireQuran_NeverBisectArabicWords()
    {
        using var conn = new SqliteConnection($"Data Source={ContentPath};Mode=ReadOnly;Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT sura_no, aya_no, tafsir_markup FROM reader_tafsir WHERE tafsir_markup LIKE '%<span%'";

        using var reader = cmd.ExecuteReader();
        var checkedCount = 0;
        var pattern = new System.Text.RegularExpressions.Regex(@"(.)<span\s+class=['""]aya['""]\s*>(.)(.*?)(.)</span>(.)", System.Text.RegularExpressions.RegexOptions.Singleline);

        while (reader.Read())
        {
            var surah = reader.GetInt32(0);
            var ayah = reader.GetInt32(1);
            var markup = reader.GetString(2);

            foreach (System.Text.RegularExpressions.Match match in pattern.Matches(markup))
            {
                checkedCount++;
                var beforeChar = match.Groups[1].Value[0];
                var afterChar = match.Groups[5].Value[0];

                // Neither beforeChar nor afterChar should be a connecting Arabic letter
                // (they must be spaces, colons, hyphens, or commas)
                var punctuation = " ،-.:؛\n\r\t ";
                if (beforeChar >= '\u0600' && beforeChar <= '\u06FF')
                {
                    Assert.Contains(beforeChar, punctuation);
                }
                if (afterChar >= '\u0600' && afterChar <= '\u06FF')
                {
                    Assert.Contains(afterChar, punctuation);
                }
            }
        }

        Assert.True(checkedCount >= 40, "Must have checked all 40+ ayah spans across the dataset");
    }

    [Fact]
    public void RTLNavigation_ProgressionRules_AdvanceCorrectlyWithoutRepetition()
    {
        // Spreads in 604-page Madinah Mushaf:
        // Spread 1: Page 1 (Right) + Page 2 (Left) -> SpreadStart = 1
        Assert.Equal(1, ReadingLayout.SpreadStart(1));
        Assert.Equal(1, ReadingLayout.SpreadStart(2));

        // Advance to next spread: must go to 3 (Pages 3 & 4)
        var nextFrom1 = ReadingLayout.NextPage(1, spread: true);
        Assert.Equal(3, nextFrom1);
        var nextFrom2 = ReadingLayout.NextPage(2, spread: true);
        Assert.Equal(3, nextFrom2);

        // Next spread from 3: must go to 5 (Pages 5 & 6)
        var nextFrom3 = ReadingLayout.NextPage(3, spread: true);
        Assert.Equal(5, nextFrom3);

        // Previous spread from 5: must return to 3
        var prevFrom5 = ReadingLayout.PreviousPage(5, spread: true);
        Assert.Equal(3, prevFrom5);

        // Previous spread from 3: must return to 1
        var prevFrom3 = ReadingLayout.PreviousPage(3, spread: true);
        Assert.Equal(1, prevFrom3);

        // Bounded boundaries: Page 1 cannot go previous, Page 604 cannot go beyond 604
        Assert.Equal(1, ReadingLayout.PreviousPage(1, spread: true));
        Assert.Equal(604, ReadingLayout.NextPage(603, spread: true));
        Assert.Equal(604, ReadingLayout.NextPage(604, spread: true));
    }

    [Fact]
    public void MushafPageComposition_OpeningSpread_HasProminentScaleAndFits()
    {
        var page1Verses = new List<Verse>
        {
            new(new(1, 1), "الفاتحة", 1, 1, "بِسْمِ اللَّهِ الرَّحْمَٰنِ الرَّحِيمِ"),
            new(new(1, 2), "الفاتحة", 1, 1, "الْحَمْدُ لِلَّهِ رَبِّ الْعَالَمِينَ"),
            new(new(1, 3), "الفاتحة", 1, 1, "الرَّحْمَٰنِ الرَّحِيمِ"),
            new(new(1, 4), "الفاتحة", 1, 1, "مَالِكِ يَوْمِ الدِّينِ"),
            new(new(1, 5), "الفاتحة", 1, 1, "إِيَّاكَ نَعْبُدُ وَإِيَّاكَ نَسْتَعِينُ"),
            new(new(1, 6), "الفاتحة", 1, 1, "اهْدِنَا الصِّرَاطَ الْمُسْتَقِيمَ"),
            new(new(1, 7), "الفاتحة", 1, 1, "صِرَاطَ الَّذِينَ أَنْعَمْتَ عَلَيْهِمْ غَيْرِ الْمَغْضُوبِ عَلَيْهِمْ وَلَا الضَّالِّينَ")
        };

        var page2Verses = new List<Verse>
        {
            new(new(2, 1), "البقرة", 2, 1, "الم"),
            new(new(2, 2), "البقرة", 2, 1, "ذَٰلِكَ الْكِتَابُ لَا رَيْبَ ۛ فِيهِ ۛ هُدًى لِّلْمُتَّقِينَ"),
            new(new(2, 3), "البقرة", 2, 1, "الَّذِينَ يُؤْمِنُونَ بِالْغَيْبِ وَيُقِيمُونَ الصَّلَاةَ وَمِمَّا رَزَقْنَاهُمْ يُنفِقُونَ"),
            new(new(2, 4), "البقرة", 2, 1, "وَالَّذِينَ يُؤْمِنُونَ بِمَا أُنزِلَ إِلَيْكَ وَمَا أُنزِلَ مِن قَبْلِكَ وَبِالْآخِرَةِ هُمْ يُوقِنُونَ"),
            new(new(2, 5), "البقرة", 2, 1, "أُولَٰئِكَ عَلَىٰ هُدًى مِّن رَّبِّهِمْ ۖ وَأُولَٰئِكَ هُمُ الْمُفْلِحُونَ")
        };

        var (scale, fits) = ReadingLayout.CalculateSpreadScale(page1Verses, page2Verses, 600, 750);
        Assert.True(fits);
        Assert.Equal(28.0, scale);
    }
}
