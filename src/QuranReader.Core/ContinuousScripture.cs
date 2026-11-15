using System.Globalization;

namespace QuranReader.Core;

/// <summary>A lossless span into an authoritative whole ayah, never a new scripture value.</summary>
public sealed record ScriptureSpan(Verse Source, int Start, int Length)
{
    public int End => Start + Length;
    public string Text => Source.Text.Substring(Start, Length);
}

/// <summary>One bounded source window. Canonical page numbers never cause a flow break.</summary>
public sealed class ContinuousScripture
{
    public const int MaxVerses = 256;
    // Keep native paragraph/shaping allocation bounded during sustained turns.
    // Exhausting this window loads the next exact source cursor, never a page jump.
    public const int MaxCodeUnits = 16384;
    public IReadOnlyList<ScriptureSpan> Spans { get; }
    public int CodeUnits => Spans.Sum(s => s.Length);
    public ReadingPosition Start => new(Spans[0].Source.Id, TextOffset: Spans[0].Start);
    public ReadingPosition End => new(Spans[^1].Source.Id, TextOffset: Spans[^1].End);
    public bool ReachesEnd => Spans[^1].Source.Id == new AyahId(114, 6) && Spans[^1].End == Spans[^1].Source.Text.Length;

    public ContinuousScripture(IReadOnlyList<Verse> verses, ReadingPosition start, ReadingPosition? end = null)
    {
        if (verses.Count is 0 or > MaxVerses) throw new ArgumentException("A continuous window requires 1–256 source ayat.", nameof(verses));
        var spans = new List<ScriptureSpan>();
        AyahId? previous = null;
        foreach (var verse in verses)
        {
            if (previous is { } prior && NextAyah(prior) != verse.Id)
                throw new ResourceException("Continuous source is missing, duplicated or out of order.");
            previous = verse.Id;
            if (Compare(verse.Id, start.Ayah) < 0) continue;
            if (end is not null && Compare(verse.Id, end.Ayah) > 0) break;
            var first = verse.Id == start.Ayah ? start.TextOffset : 0;
            var last = end is not null && verse.Id == end.Ayah ? end.TextOffset : verse.Text.Length;
            RequireBoundary(verse.Text, first);
            RequireBoundary(verse.Text, last);
            if (last < first) throw new ResourceException("Continuous source range is reversed.");
            if (last == first) continue;
            if (spans.Count > 0 && spans.Sum(s => s.Length) + last - first > MaxCodeUnits) break;
            spans.Add(new(verse, first, last - first));
        }
        if (spans.Count == 0 || spans[0].Source.Id != start.Ayah || spans[0].Start != start.TextOffset)
            throw new ResourceException("The requested continuous source anchor is unavailable.");
        Spans = spans;
    }

    public static int Compare(AyahId a, AyahId b) => a.Surah != b.Surah ? a.Surah.CompareTo(b.Surah) : a.Ayah.CompareTo(b.Ayah);

    public static AyahId? NextAyah(AyahId id) => id.Ayah < SurahMetadata.TotalAyat(id.Surah)
        ? new(id.Surah, id.Ayah + 1) : id.Surah < 114 ? new(id.Surah + 1, 1) : null;

    public static bool IsBoundary(string source, int offset) => offset == source.Length ||
        (offset >= 0 && offset < source.Length && Array.BinarySearch(StringInfo.ParseCombiningCharacters(source), offset) >= 0);

    public static void RequireBoundary(string source, int offset)
    {
        if (!IsBoundary(source, offset)) throw new ResourceException("A reading boundary would split an Arabic grapheme cluster; composition was rejected.");
    }

    /// <summary>Partition a source range using measured native break cursors; no estimates or normalization.</summary>
    public IReadOnlyList<ScriptureSpan> Between(ReadingPosition first, ReadingPosition last)
    {
        var result = new List<ScriptureSpan>();
        foreach (var span in Spans)
        {
            if (Compare(span.Source.Id, first.Ayah) < 0 || Compare(span.Source.Id, last.Ayah) > 0) continue;
            var a = span.Source.Id == first.Ayah ? Math.Max(span.Start, first.TextOffset) : span.Start;
            var b = span.Source.Id == last.Ayah ? Math.Min(span.End, last.TextOffset) : span.End;
            RequireBoundary(span.Source.Text, a);
            RequireBoundary(span.Source.Text, b);
            if (b > a) result.Add(new(span.Source, a, b - a));
        }
        return result;
    }
}
