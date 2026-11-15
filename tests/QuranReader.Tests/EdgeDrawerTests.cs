using QuranReader.Core;
using QuranReader.Data;

namespace QuranReader.Tests;

public sealed class EdgeDrawerTests
{
    private static ContentRepository Content() => new(Path.Combine(AppContext.BaseDirectory, "Content", "quran_reader_candidate.sqlite3"));
    private sealed class MemoryStore : IUserStateStore
    {
        public UserState State { get; set; } = new(new(new(2, 253)), new(Mode: ReadingMode.AdaptiveBook, Presentation: TafsirPresentation.EdgeDrawer), [], new(2, 255));
        public Task<UserState> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(State);
        public Task SaveAsync(UserState state, CancellationToken cancellationToken = default) { State = state; return Task.CompletedTask; }
    }

    [Fact]
    public async Task RestoredAnchorAndSavedSelectionDoNotCreateAnActiveEdgeSelection()
    {
        using var content = Content();
        using var model = new ReaderViewModel(content, new MemoryStore());
        await model.InitializeAsync();
        var anchor = model.Position;
        Assert.NotNull(model.SelectedVerse);
        Assert.False(model.HasActiveSelection);
        await model.UpdateSettingsAsync(model.Settings with { TafsirOpen = true });
        Assert.Null(model.ActiveTafsirVerse);
        Assert.Empty(model.Tafsir);
        Assert.Equal(anchor, model.Position);
    }

    [Fact]
    public async Task StudyRestoresItsCommentaryEvenWhenTheBookPreferenceIsEdgeDrawer()
    {
        using var content = Content();
        var store = new MemoryStore();
        store.State = store.State with { Settings = store.State.Settings with { Mode = ReadingMode.Tafsir, TafsirOpen = true } };
        using var model = new ReaderViewModel(content, store);
        await model.InitializeAsync();
        Assert.False(model.HasActiveSelection);
        Assert.Equal(new AyahId(2, 255), model.SelectedVerse?.Id);
        Assert.Equal(await content.GetTafsirAsync(new(2, 255)), model.Tafsir);
        await model.UpdateSettingsAsync(model.Settings with { Mode = ReadingMode.AdaptiveBook, DrawerPinned = true });
        Assert.Null(model.ActiveTafsirVerse);
        Assert.Empty(model.Tafsir);
    }

    [Fact]
    public async Task ReadingOnlyAyahJumpDoesNotSilentlyActivateEdgeContext()
    {
        using var content = Content();
        using var model = new ReaderViewModel(content, new MemoryStore());
        await model.InitializeAsync();
        await model.UpdateSettingsAsync(model.Settings with { TafsirOpen = true });
        await model.GoToAsync(new(2, 255), activateSelection: false);
        Assert.Equal(new AyahId(2, 255), model.Position.Ayah);
        Assert.Null(model.ActiveTafsirVerse);
        Assert.Empty(model.Tafsir);
        await model.GoToAsync(new(2, 255));
        Assert.Equal(new AyahId(2, 255), model.ActiveTafsirVerse?.Id);
        Assert.Equal(await content.GetTafsirAsync(new(2, 255)), model.Tafsir);
    }

    [Fact]
    public async Task ExplicitSelectionSurvivesReopenAndContinuousNavigationWithoutMovingAnchor()
    {
        using var content = Content();
        using var model = new ReaderViewModel(content, new MemoryStore());
        await model.InitializeAsync();
        var anchor = model.Position;
        var verse = model.Continuous!.Spans.First(s => s.Source.Id == new AyahId(2, 255)).Source;
        await model.SelectAsync(verse);
        Assert.Equal(anchor, model.Position);
        Assert.Same(verse, model.ActiveTafsirVerse);
        var expected = await content.GetTafsirAsync(verse.Id);
        Assert.Equal(expected, model.Tafsir);
        await model.UpdateSettingsAsync(model.Settings with { TafsirOpen = false });
        await model.UpdateSettingsAsync(model.Settings with { TafsirOpen = true });
        Assert.Equal(expected, model.Tafsir);
        await model.UpdateSettingsAsync(model.Settings with { DrawerPinned = true });
        await model.UpdateSettingsAsync(model.Settings with { Mode = ReadingMode.Mushaf });
        Assert.Same(verse, model.ActiveTafsirVerse);
        Assert.Equal(expected, model.Tafsir);
        await model.UpdateSettingsAsync(model.Settings with { Mode = ReadingMode.AdaptiveBook });
        Assert.Equal(expected, model.Tafsir);
        await model.LoadContinuousWindowAsync(new(new(3, 1)));
        Assert.Same(verse, model.ActiveTafsirVerse);
        Assert.Equal(expected, model.Tafsir);
    }

    [Fact]
    public async Task ClearAndCanonicalNavigationCannotShowAnAutomaticVerseAsActive()
    {
        using var content = Content();
        using var model = new ReaderViewModel(content, new MemoryStore());
        await model.InitializeAsync();
        await model.SelectAsync(model.Continuous!.Spans[0].Source);
        var anchor = model.Position;
        await model.ClearSelectionAsync();
        Assert.Null(model.ActiveTafsirVerse);
        Assert.Empty(model.Tafsir);
        Assert.Equal(anchor, model.Position);
        await model.NavigateAsync(604);
        Assert.Null(model.ActiveTafsirVerse);
        Assert.Empty(model.Tafsir);
        Assert.Equal(604, model.Page);
    }

    [Fact]
    public async Task EdgePreferenceRoundTripsThroughTheExistingStore()
    {
        var folder = Path.Combine(Path.GetTempPath(), "QuranReader.EdgeTests", Guid.NewGuid().ToString());
        try
        {
            var store = new UserStateStore(Path.Combine(folder, "user.sqlite3"));
            var expected = new MemoryStore().State;
            await store.SaveAsync(expected);
            var actual = await store.LoadAsync();
            Assert.Equal(TafsirPresentation.EdgeDrawer, actual.Settings.Presentation);
            Assert.Equal(expected.Position, actual.Position);
            Assert.Equal(expected.SelectedAyah, actual.SelectedAyah);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SupersededTafsirCannotReplaceTheNewSelectionOrAnEmptyState(bool clear)
    {
        using var content = Content();
        var delayed = new DelayedTafsir(content);
        using var model = new ReaderViewModel(delayed, new MemoryStore());
        await model.InitializeAsync();
        await model.UpdateSettingsAsync(model.Settings with { TafsirOpen = true });
        var first = model.Continuous!.Spans.First(s => s.Source.Id == new AyahId(2, 255)).Source;
        var second = model.Continuous.Spans.First(s => s.Source.Id == new AyahId(2, 256)).Source;
        var pending = model.SelectAsync(first);
        await delayed.Started.Task;
        if (clear) await model.ClearSelectionAsync();
        else await model.SelectAsync(second);
        delayed.Release.TrySetResult();
        await pending;
        Assert.Equal(clear ? null : second.Id, model.ActiveTafsirVerse?.Id);
        Assert.Equal(clear ? "" : await content.GetTafsirAsync(second.Id), model.Tafsir);
    }

    private sealed class DelayedTafsir(ContentRepository inner) : IContinuousContentRepository
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task ValidateAsync(CancellationToken cancellationToken = default) => inner.ValidateAsync(cancellationToken);
        public Task<IReadOnlyList<Surah>> GetSurahsAsync(CancellationToken cancellationToken = default) => inner.GetSurahsAsync(cancellationToken);
        public Task<IReadOnlyList<Verse>> GetPageAsync(int page, CancellationToken cancellationToken = default) => inner.GetPageAsync(page, cancellationToken);
        public Task<int?> FindPageAsync(AyahId id, CancellationToken cancellationToken = default) => inner.FindPageAsync(id, cancellationToken);
        public Task<IReadOnlyList<Verse>> GetSequenceAsync(AyahId start, int limit, CancellationToken cancellationToken = default) => inner.GetSequenceAsync(start, limit, cancellationToken);
        public Task<IReadOnlyList<Verse>> GetPrecedingAsync(AyahId before, int limit, CancellationToken cancellationToken = default) => inner.GetPrecedingAsync(before, limit, cancellationToken);
        public async Task<string> GetTafsirAsync(AyahId id, CancellationToken cancellationToken = default)
        {
            if (id == new AyahId(2, 255)) { Started.TrySetResult(); await Release.Task; }
            return await inner.GetTafsirAsync(id, cancellationToken);
        }
    }
}
