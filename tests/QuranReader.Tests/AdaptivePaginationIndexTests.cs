using QuranReader.Core;
using QuranReader.Data;

namespace QuranReader.Tests;

public sealed class AdaptivePaginationIndexTests
{
    private static AdaptiveLayoutKey Layout(double scale = 28, double dpi = 1) => new(1300, 640, scale, dpi, true, ContentRepository.PinnedSha256, PublisherFont.PinnedSha256);

    private static async Task<ReadingLeaf[]> Measurements()
    {
        using var source = new ContentRepository(Path.Combine(AppContext.BaseDirectory, "Content", "quran_reader_candidate.sqlite3"));
        await source.ValidateAsync();
        var verses = await source.GetSequenceAsync(new(2, 6), 24);
        return verses.Take(22).Select((v, i) => new ReadingLeaf(i, new(v.Id),
            new(ContinuousScripture.NextAyah(v.Id)!.Value), [new(v, 0, v.Text.Length)], $"test-measurement-{i}")).ToArray();
    }

    [Fact]
    public async Task BothDirectionsUseTheSameImmutableTwentyTwoLeafTable()
    {
        var measured = await Measurements();
        var index = new AdaptivePaginationIndex(Layout());
        var first = index.Append(AdaptivePaginationIndex.Origin, measured.Take(12).ToArray(), false);
        var second = index.Append(first.End, measured.Skip(12).ToArray(), false);
        var original = Enumerable.Range(0, 11).Select(i => index.Find(2 + i * 2)!.Leaves.Single(l => l.Number == 2 + i * 2)).ToArray();
        foreach (var i in Enumerable.Range(0, 11).Reverse())
            Assert.Same(original[i], index.Find(2 + i * 2)!.Leaves.Single(l => l.Number == 2 + i * 2));
        Assert.Equal(second.End, index.NextSource);
        Assert.Equal(24, index.Count);
        Assert.Equal(new ReadingPosition(new(1, 1)), AdaptivePaginationIndex.StreamOrigin);
        Assert.Throws<ResourceException>(() => index.Append(AdaptivePaginationIndex.Origin, measured.Take(2).ToArray(), false));
    }

    [Fact]
    public async Task ReplayRejectsChangedBoundariesAndColdReconstructionMatches()
    {
        var measured = await Measurements();
        var index = new AdaptivePaginationIndex(Layout());
        var window = index.Append(AdaptivePaginationIndex.Origin, measured, false);
        index.Verify(window, measured);
        var corrupt = measured.ToArray();
        corrupt[1] = corrupt[1] with { End = corrupt[2].End };
        Assert.Throws<ResourceException>(() => index.Verify(window, corrupt));
        corrupt = measured.ToArray(); corrupt[1] = corrupt[1] with { LayoutSignature = "changed-line-geometry" };
        Assert.Throws<ResourceException>(() => index.Verify(window, corrupt));
        var cold = new AdaptivePaginationIndex(Layout());
        Assert.Equal(window.Leaves, cold.Append(AdaptivePaginationIndex.Origin, measured, false).Leaves);
        Assert.NotEqual(index.Layout, new AdaptivePaginationIndex(Layout(32)).Layout);
        Assert.NotEqual(index.Layout, new AdaptivePaginationIndex(Layout(dpi: 1.25)).Layout);
    }

    [Fact]
    public async Task SpreadCursorAlignmentAndBrokenCoverageAreExplicit()
    {
        var measured = await Measurements();
        var index = new AdaptivePaginationIndex(Layout());
        var window = index.Append(AdaptivePaginationIndex.Origin, measured, false);
        Assert.Equal(6, index.Locate(window, measured[5].Start));
        Assert.Same(window, index.Find(measured[5].Start));
        var missing = measured.Take(2).ToArray(); missing[1] = measured[2];
        Assert.Throws<ResourceException>(() => new AdaptivePaginationIndex(Layout()).Append(AdaptivePaginationIndex.Origin, missing, false));
        Assert.Throws<ResourceException>(() => new AdaptivePaginationIndex(Layout()).Append(AdaptivePaginationIndex.Origin, measured.Take(3).ToArray(), false));
    }
}
