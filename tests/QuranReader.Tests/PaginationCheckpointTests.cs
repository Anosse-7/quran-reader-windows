using System.Text.Json.Nodes;
using QuranReader.Core;
using QuranReader.Data;

namespace QuranReader.Tests;

public sealed class PaginationCheckpointTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "QuranReader.CheckpointTests", Guid.NewGuid().ToString("N"));
    private static AdaptiveLayoutKey Layout(double scale = 28, double dpi = 1, double width = 1300) =>
        new(width, 640, scale, dpi, true, ContentRepository.PinnedSha256, PublisherFont.PinnedSha256);
    private PaginationCheckpointStore Store(string compatibility = "test-renderer-v1") => new(directory, compatibility, (_, _) => Task.FromResult(true));
    private static ReadingLeaf[] Measurements() => Enumerable.Range(6, 4).Select((a, i) =>
        new ReadingLeaf(i, new(new(2, a)), new(new(2, a + 1)), [], new string('A', 64))).ToArray();
    private static AdaptivePaginationIndex Index(AdaptiveLayoutKey? layout = null)
    {
        var index = new AdaptivePaginationIndex(layout ?? Layout());
        index.AppendBoundaries(AdaptivePaginationIndex.Origin, Measurements(), false);
        return index;
    }

    [Fact]
    public void UnseenBoundariesAreNotGeometryAndDisplayedBaselineCannotBeReplaced()
    {
        var index = Index(); var unseen = index.Windows[0]; var measured = Measurements();
        Assert.False(unseen.HasGeometry);
        Assert.Throws<ResourceException>(() => index.Verify(unseen, measured));
        var sealedWindow = index.VerifyOrRecordGeometry(unseen, measured);
        Assert.True(sealedWindow.HasGeometry); Assert.False(unseen.HasGeometry);
        Assert.Same(sealedWindow, index.VerifyOrRecordGeometry(sealedWindow, measured));
        measured[0] = measured[0] with { LayoutSignature = new string('B', 64) };
        Assert.Throws<ResourceException>(() => index.VerifyOrRecordGeometry(sealedWindow, measured));
        Assert.Throws<ResourceException>(() => index.VerifyOrRecordGeometry(unseen, measured));
    }

    [Fact]
    public async Task FreshStoreReconstructsExactBoundariesAndSealedGeometry()
    {
        var index = Index(); index.VerifyOrRecordGeometry(index.Windows[0], Measurements());
        await Store().SaveAsync(index.Layout, index.Windows.ToArray());
        var restored = await Store().LoadAsync(index.Layout);
        Assert.NotNull(restored); Assert.Equal(index.NextSource, restored.NextSource);
        Assert.Equal(index.Windows[0].Leaves, restored.Windows[0].Leaves);
        restored.Verify(restored.Windows[0], Measurements());
    }

    [Fact]
    public async Task CorruptPartialAndVersionMismatchesBecomeCacheMisses()
    {
        var store = Store(); var index = Index();
        foreach (var corruption in new[] { "truncated", "version", "checksum", "null-payload" })
        {
            await store.SaveAsync(index.Layout, index.Windows.ToArray());
            var path = store.FilePath(index.Layout);
            var node = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
            if (corruption == "version") node["Version"] = 900;
            if (corruption == "checksum") node["Digest"] = "corrupt";
            if (corruption == "null-payload") node["Payload"] = null;
            await File.WriteAllTextAsync(path, corruption == "truncated" ? "{\"Version\":" : node.ToJsonString());
            Assert.Null(await store.LoadAsync(index.Layout)); Assert.StartsWith("rejected:", store.LastResult);
        }
    }

    [Fact]
    public async Task OversizedFileIsRejectedBeforeDeserialization()
    {
        var store = Store(); Directory.CreateDirectory(directory);
        using (var file = File.Create(store.FilePath(Layout()))) file.SetLength(PaginationCheckpointStore.MaximumBytes + 1L);
        Assert.Null(await store.LoadAsync(Layout())); Assert.Contains("Oversized", store.LastResult);
    }

    [Fact]
    public async Task IncompatibleLayoutAndNativeCompatibilityCannotHit()
    {
        var index = Index(); var store = Store(); await store.SaveAsync(index.Layout, index.Windows.ToArray());
        foreach (var layout in new[] { Layout(32), Layout(dpi: 1.25), Layout(width: 1100), Layout() with { Policy = "future" }, Layout() with { FontPin = "other-font" } })
            Assert.Null(await store.LoadAsync(layout));
        Assert.Null(await Store("other-native-renderer").LoadAsync(index.Layout));
    }

    [Fact]
    public async Task OffsetValidationFailureRejectsOtherwiseIntactFile()
    {
        var index = Index(); await Store().SaveAsync(index.Layout, index.Windows.ToArray());
        var rejecting = new PaginationCheckpointStore(directory, "test-renderer-v1", (_, _) => Task.FromResult(false));
        Assert.Null(await rejecting.LoadAsync(index.Layout)); Assert.Contains("pinned source", rejecting.LastResult);
    }

    [Fact]
    public async Task InterruptedWriteDoesNotReplaceValidCheckpointAndTemporaryFilesAreIgnored()
    {
        var index = Index(); var store = Store(); await store.SaveAsync(index.Layout, index.Windows.ToArray());
        var before = await File.ReadAllBytesAsync(store.FilePath(index.Layout));
        await File.WriteAllTextAsync(store.FilePath(index.Layout) + ".interrupted.tmp", "partial");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(index.Layout, [], cancellation.Token));
        Assert.Equal(before, await File.ReadAllBytesAsync(store.FilePath(index.Layout)));
        Assert.NotNull(await Store().LoadAsync(index.Layout));
    }

    [Fact]
    public async Task GenerationEvictionIsBoundedAndNewestRemainsReadable()
    {
        var store = Store();
        for (var i = 0; i < 12; i++)
        {
            var index = Index(Layout(width: 1000 + i)); await store.SaveAsync(index.Layout, index.Windows.ToArray());
        }
        Assert.Equal(PaginationCheckpointStore.MaximumFiles, Directory.GetFiles(directory, "*.json").Length);
        Assert.NotNull(await store.LoadAsync(Layout(width: 1011)));
        Assert.Null(await store.LoadAsync(Layout(width: 1000)));
    }

    [Fact]
    public void RestoreRejectsGapsDuplicateOrdinalsAndMixedGeometry()
    {
        var index = Index(); var window = index.Windows[0];
        Assert.Throws<ResourceException>(() => AdaptivePaginationIndex.Restore(index.Layout, [window with { FirstLeaf = 4 }]));
        var leaves = window.Leaves.ToArray(); leaves[1] = leaves[1] with { Number = 2 };
        Assert.Throws<ResourceException>(() => AdaptivePaginationIndex.Restore(index.Layout, [window with { Leaves = leaves }]));
        leaves = window.Leaves.ToArray(); leaves[0] = leaves[0] with { LayoutSignature = new string('A', 64) };
        Assert.Throws<ResourceException>(() => AdaptivePaginationIndex.Restore(index.Layout, [window with { Leaves = leaves }]));
        leaves = window.Leaves.ToArray(); leaves[1] = leaves[1] with { Start = new(new(2, 8)) };
        Assert.Throws<ResourceException>(() => AdaptivePaginationIndex.Restore(index.Layout, [window with { Leaves = leaves }]));
    }

    [Fact]
    public async Task PinnedSourceValidationRejectsSplitGraphemeAndOutOfRangeAyah()
    {
        using var content = new ContentRepository(Path.Combine(AppContext.BaseDirectory, "Content", "quran_reader_candidate.sqlite3"));
        await content.ValidateAsync();
        var verse = (await content.GetSequenceAsync(new(2, 6), 1))[0];
        var split = Enumerable.Range(0, verse.Text.Length).First(i => !ContinuousScripture.IsBoundary(verse.Text, i));
        Assert.True(await content.ValidateCheckpointPositionsAsync([new(verse.Id)]));
        Assert.False(await content.ValidateCheckpointPositionsAsync([new(verse.Id, TextOffset: split)]));
        Assert.False(await content.ValidateCheckpointPositionsAsync([new(new(2, 900))]));
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
