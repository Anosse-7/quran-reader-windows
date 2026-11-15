namespace QuranReader.Core;

public static class SurahMetadata
{
    // Canonical 28 Medinan surahs in King Fahd Complex / standard Uthmanic codex
    private static readonly HashSet<int> MedinanSurahs =
    [
        2, 3, 4, 5, 8, 9, 13, 22, 24, 33, 47, 48, 49, 55, 57, 58, 59, 60, 61, 62, 63, 64, 65, 66, 76, 98, 99, 110
    ];

    // Total verses per surah (1 to 114) in the standard KFGQPC Hafs edition
    private static readonly int[] VerseCounts =
    [
        0, // 0-index unused
        7, 286, 200, 176, 120, 165, 206, 75, 129, 109,
        123, 111, 43, 52, 99, 128, 111, 110, 98, 135,
        112, 78, 118, 64, 77, 227, 93, 88, 69, 60,
        34, 30, 73, 54, 45, 83, 182, 88, 75, 85,
        54, 53, 89, 59, 37, 35, 38, 29, 18, 45,
        60, 49, 62, 55, 78, 96, 29, 22, 24, 13,
        14, 11, 11, 18, 12, 12, 30, 52, 52, 44,
        28, 28, 20, 56, 40, 31, 50, 40, 46, 42,
        29, 19, 36, 25, 22, 17, 19, 26, 30, 20,
        15, 21, 11, 8, 8, 19, 5, 8, 8, 11,
        11, 8, 3, 9, 5, 4, 7, 3, 6, 3,
        5, 4, 5, 6
    ];

    private static readonly string[] JuzNamesArabic =
    [
        "", // 0-index unused
        "الجُزْءُ الأَوَّلُ", "الجُزْءُ الثَّانِي", "الجُزْءُ الثَّالِثُ", "الجُزْءُ الرَّابِعُ", "الجُزْءُ الخَامِسُ",
        "الجُزْءُ السَّادِسُ", "الجُزْءُ السَّابِعُ", "الجُزْءُ الثَّامِنُ", "الجُزْءُ التَّاسِعُ", "الجُزْءُ العَاشِرُ",
        "الجُزْءُ الحَادِي عَشَرَ", "الجُزْءُ الثَّانِي عَشَرَ", "الجُزْءُ الثَّالِثَ عَشَرَ", "الجُزْءُ الرَّابِعَ عَشَرَ", "الجُزْءُ الخَامِسَ عَشَرَ",
        "الجُزْءُ السَّادِسَ عَشَرَ", "الجُزْءُ السَّابِعَ عَشَرَ", "الجُزْءُ الثَّامِنَ عَشَرَ", "الجُزْءُ التَّاسِعَ عَشَرَ", "الجُزْءُ العِشْرُونَ",
        "الجُزْءُ الحَادِي وَالعِشْرُونَ", "الجُزْءُ الثَّانِي وَالعِشْرُونَ", "الجُزْءُ الثَّالِثُ وَالعِشْرُونَ", "الجُزْءُ الرَّابِعُ وَالعِشْرُونَ", "الجُزْءُ الخَامِسُ وَالعِشْرُونَ",
        "الجُزْءُ السَّادِسُ وَالعِشْرُونَ", "الجُزْءُ السَّابِعُ وَالعِشْرُونَ", "الجُزْءُ الثَّامِنُ وَالعِشْرُونَ", "الجُزْءُ التَّاسِعُ وَالعِشْرُونَ", "الجُزْءُ الثَّلَاثُونَ"
    ];

    public const string CanonicalBasmalah = "بِسۡمِ ٱللَّهِ ٱلرَّحۡمَٰنِ ٱلرَّحِيمِ";

    public static bool IsMedinan(int surah) => MedinanSurahs.Contains(surah);

    public static string RevelationTypeArabic(int surah) => IsMedinan(surah) ? "مَدَنِيَّةٌ" : "مَكِّيَّةٌ";

    public static int TotalAyat(int surah) => surah is >= 1 and <= 114 ? VerseCounts[surah] : 0;

    // Surah 1 (Al-Fatihah) has Basmalah as numbered Ayah 1; Surah 9 (At-Tawbah) has no Basmalah.
    public static bool HasBasmalahHeader(int surah) => surah is not 1 and not 9;

    public static string JuzNameArabic(int juz) => juz is >= 1 and <= 30 ? JuzNamesArabic[juz] : $"الجزء {juz}";

    public static string ToEasternArabicDigits(int number)
    {
        var s = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var chars = new char[s.Length];
        for (var i = 0; i < s.Length; i++)
        {
            chars[i] = (char)('٠' + (s[i] - '0'));
        }
        return new string(chars);
    }
}
