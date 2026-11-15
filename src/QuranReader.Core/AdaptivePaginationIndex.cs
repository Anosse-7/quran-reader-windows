namespace QuranReader.Core;

/// <summary>Identity of a native layout, independent of navigation history and presentation theme.</summary>
public sealed record AdaptiveLayoutKey(double Width, double Height, double Scale, double Dpi, bool Spread,
    string ContentPin, string FontPin, double LineHeightRatio = ReadingLayout.MushafLineHeightRatio,
    string Policy = "canonical-stream-v3-opening-prefix");

public sealed record AdaptiveLeafBoundary(int Number, ReadingPosition Start, ReadingPosition End, string LayoutSignature);
public sealed record AdaptiveWindowBoundary(int FirstLeaf, ReadingPosition Start, ReadingPosition End,
    IReadOnlyList<AdaptiveLeafBoundary> Leaves, bool Final)
{
    public bool HasGeometry => Leaves.All(l => !string.IsNullOrEmpty(l.LayoutSignature));
}

/// <summary>Append-only measurements from one canonical origin. Both directions consult the same table.</summary>
public sealed class AdaptivePaginationIndex(AdaptiveLayoutKey layout)
{
    public static ReadingPosition StreamOrigin { get; } = new(new(1, 1));
    // The dedicated opening policy owns the deterministic 1:1–2:1 and 2:1–2:6
    // leaves. Ordinary continuous composition continues after that fixed prefix.
    public const int OpeningLeafCount = 2;
    public static ReadingPosition Origin { get; } = new(new(2, 6));
    private readonly List<AdaptiveWindowBoundary> windows = [];
    public AdaptiveLayoutKey Layout { get; } = layout;
    public IReadOnlyList<AdaptiveWindowBoundary> Windows => windows.AsReadOnly();
    public int Count => windows.Count == 0 ? OpeningLeafCount : windows[^1].FirstLeaf + windows[^1].Leaves.Count;
    public bool Complete => windows.Count > 0 && windows[^1].Final;
    public ReadingPosition NextSource => windows.Count == 0 ? Origin : windows[^1].End;

    public AdaptiveWindowBoundary? Find(ReadingPosition cursor) => windows.FirstOrDefault(w =>
        ContinuousPagination.Compare(cursor, w.Start) >= 0 && (ContinuousPagination.Compare(cursor, w.End) < 0 || w.Final && ContinuousPagination.Compare(cursor, w.End) == 0));

    public AdaptiveWindowBoundary? Find(int leaf) => windows.FirstOrDefault(w => leaf >= w.FirstLeaf && leaf < w.FirstLeaf + w.Leaves.Count);

    public static AdaptivePaginationIndex Restore(AdaptiveLayoutKey layout, IReadOnlyList<AdaptiveWindowBoundary> checkpoints)
    {
        if (checkpoints.Count > 1024) throw new ResourceException("Too many adaptive checkpoint windows.");
        var result = new AdaptivePaginationIndex(layout);
        foreach (var window in checkpoints)
        {
            if (window is null || window.Leaves is null || window.Leaves.Count is < 1 or > 16 || window.FirstLeaf != result.Count)
                throw new ResourceException("Invalid adaptive checkpoint window.");
            foreach (var leaf in window.Leaves)
            {
                if (leaf is null || !ValidCursor(leaf.Start) || !ValidCursor(leaf.End) || leaf.Number < window.FirstLeaf || leaf.LayoutSignature is null ||
                    leaf.LayoutSignature.Length != 0 && (leaf.LayoutSignature.Length != 64 || !leaf.LayoutSignature.All(Uri.IsHexDigit)))
                    throw new ResourceException("Invalid adaptive checkpoint leaf.");
            }
            if (window.Leaves.Any(l => l.LayoutSignature.Length == 0) && window.Leaves.Any(l => l.LayoutSignature.Length != 0))
                throw new ResourceException("Partial adaptive checkpoint geometry.");
            var measured = window.Leaves.Select(l => new ReadingLeaf(l.Number, l.Start, l.End, [], l.LayoutSignature)).ToArray();
            var added = window.HasGeometry ? result.Append(window.Start, measured, window.Final) : result.AppendBoundaries(window.Start, measured, window.Final);
            if (added.End != window.End || !added.Leaves.SequenceEqual(window.Leaves)) throw new ResourceException("Discontinuous adaptive checkpoint metadata.");
        }
        return result;
    }

    private static bool ValidCursor(ReadingPosition? cursor) => cursor is not null && cursor.Offset == 0 && cursor.TextOffset >= 0 &&
        cursor.Ayah.Surah is >= 1 and <= 114 && cursor.Ayah.Ayah >= 1 && cursor.Ayah.Ayah <= SurahMetadata.TotalAyat(cursor.Ayah.Surah);

    public AdaptiveWindowBoundary Append(ReadingPosition start, IReadOnlyList<ReadingLeaf> measured, bool final)
        => AppendCore(start, measured, final, true);

    // An unseen prefix needs exact native source boundaries, but has no displayed
    // geometry baseline yet. This is explicitly different from verified geometry.
    public AdaptiveWindowBoundary AppendBoundaries(ReadingPosition start, IReadOnlyList<ReadingLeaf> measured, bool final)
        => AppendCore(start, measured.Select(l => l with { LayoutSignature = "" }).ToArray(), final, false);

    private AdaptiveWindowBoundary AppendCore(ReadingPosition start, IReadOnlyList<ReadingLeaf> measured, bool final, bool requireGeometry)
    {
        if (Complete || start != NextSource || measured.Count == 0 || measured[0].Start != start)
            throw new ResourceException("Adaptive measurements do not extend the canonical boundary table.");
        if (!final && Layout.Spread && measured.Count % 2 != 0)
            throw new ResourceException("A continuation window must end on a complete spread.");
        for (var i = 0; i < measured.Count; i++)
        {
            var leaf = measured[i];
            if (requireGeometry && string.IsNullOrEmpty(leaf.LayoutSignature) || ContinuousPagination.Compare(leaf.Start, leaf.End) >= 0 || i > 0 && measured[i - 1].End != leaf.Start)
                throw new ResourceException("Adaptive leaf boundaries overlap, omit content or reverse source order.");
        }
        var result = new AdaptiveWindowBoundary(Count, start, measured[^1].End,
            Array.AsReadOnly(measured.Select((l, i) => new AdaptiveLeafBoundary(Count + i, l.Start, l.End, l.LayoutSignature)).ToArray()), final);
        windows.Add(result);
        return result;
    }

    public void Verify(AdaptiveWindowBoundary expected, IReadOnlyList<ReadingLeaf> measured)
    {
        if (!expected.HasGeometry) throw new ResourceException("Adaptive window has no displayed geometry baseline.");
        VerifyBoundaries(expected, measured);
        for (var i = 0; i < expected.Leaves.Count; i++)
            if (expected.Leaves[i].LayoutSignature != measured[i].LayoutSignature)
                throw new ResourceException($"Reconstructed adaptive leaf {expected.Leaves[i].Number + 1} has changed native geometry.");
    }

    public void VerifyBoundaries(AdaptiveWindowBoundary expected, IReadOnlyList<ReadingLeaf> measured)
    {
        if (measured.Count < expected.Leaves.Count) throw new ResourceException("Reconstructed adaptive window lost measured leaves.");
        for (var i = 0; i < expected.Leaves.Count; i++)
        {
            var before = expected.Leaves[i]; var after = measured[i];
            if (before.Start != after.Start || before.End != after.End)
                throw new ResourceException($"Reconstructed adaptive leaf {before.Number + 1} differs: {before.Start}–{before.End} / {after.Start}–{after.End}; native signatures {before.LayoutSignature} / {after.LayoutSignature}.");
        }
    }

    public AdaptiveWindowBoundary VerifyOrRecordGeometry(AdaptiveWindowBoundary expected, IReadOnlyList<ReadingLeaf> measured)
    {
        VerifyBoundaries(expected, measured);
        if (expected.HasGeometry) { Verify(expected, measured); return expected; }
        var slot = windows.IndexOf(expected);
        if (slot < 0 || expected.Leaves.Any(l => l.LayoutSignature.Length != 0) || measured.Take(expected.Leaves.Count).Any(l => string.IsNullOrEmpty(l.LayoutSignature)))
            throw new ResourceException("Cannot establish an incomplete or obsolete adaptive geometry baseline.");
        var sealedWindow = expected with { Leaves = Array.AsReadOnly(expected.Leaves.Select((l, i) => l with { LayoutSignature = measured[i].LayoutSignature }).ToArray()) };
        windows[slot] = sealedWindow;
        return sealedWindow;
    }

    public int Locate(AdaptiveWindowBoundary window, ReadingPosition cursor)
    {
        var leaf = window.Leaves.LastOrDefault(l => ContinuousPagination.Compare(l.Start, cursor) <= 0)
            ?? throw new ResourceException("The reading anchor precedes its canonical window.");
        return Layout.Spread ? leaf.Number / 2 * 2 : leaf.Number;
    }
}
