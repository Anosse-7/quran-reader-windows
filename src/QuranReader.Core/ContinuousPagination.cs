namespace QuranReader.Core;

public sealed record ReadingLeaf(int Number, ReadingPosition Start, ReadingPosition End, IReadOnlyList<ScriptureSpan> Spans, string LayoutSignature = "");

/// <summary>Validates native measurements and partitions source independently of any UI framework.</summary>
public static class ContinuousPagination
{
    public static IReadOnlyList<ReadingLeaf> FromMeasuredBreaks(ContinuousScripture source,
        IReadOnlyList<ReadingPosition> starts, ReadingPosition end)
    {
        if (starts.Count == 0 || Compare(starts[0], source.Start) != 0 || Compare(end, source.End) > 0)
            throw new ResourceException("Measured leaves do not cover the requested source prefix.");
        var result = new List<ReadingLeaf>();
        foreach (var cursor in starts.Append(end))
        {
            var span = source.Spans.FirstOrDefault(s => s.Source.Id == cursor.Ayah);
            if (span is null || cursor.TextOffset < span.Start || cursor.TextOffset > span.End)
                throw new ResourceException("A measured cursor is outside its authoritative source span.");
            ContinuousScripture.RequireBoundary(span.Source.Text, cursor.TextOffset);
        }
        for (var i = 0; i < starts.Count; i++)
        {
            var first = starts[i];
            var last = i + 1 < starts.Count ? starts[i + 1] : end;
            if (Compare(first, last) >= 0) throw new ResourceException("Measured reading leaves are empty, duplicated or reversed.");
            var spans = source.Between(first, last);
            if (spans.Count == 0) throw new ResourceException("A measured leaf has no authoritative scripture span.");
            result.Add(new(i + 1, first, last, spans));
        }
        var expected = source.Between(source.Start, end);
        if (!string.Concat(result.SelectMany(l => l.Spans).Select(s => s.Text)).Equals(string.Concat(expected.Select(s => s.Text)), StringComparison.Ordinal))
            throw new ResourceException("Measured pagination omitted or repeated source content.");
        return result;
    }

    public static int Compare(ReadingPosition first, ReadingPosition last)
    {
        var ayah = ContinuousScripture.Compare(first.Ayah, last.Ayah);
        return ayah != 0 ? ayah : first.TextOffset.CompareTo(last.TextOffset);
    }
}
