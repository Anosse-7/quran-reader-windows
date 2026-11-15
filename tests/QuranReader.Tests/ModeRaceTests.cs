using QuranReader.Core;
using QuranReader.Data;

namespace QuranReader.Tests;

public sealed class ModeRaceTests
{
    [Fact]
    public async Task PendingMushafFacingPageCannotRepopulateTafsirMode()
    {
        using var source = new ContentRepository(Path.Combine(AppContext.BaseDirectory, "Content", "quran_reader_candidate.sqlite3"));
        var delayed = new DelayedFacingPage(source);
        using var model = new ReaderViewModel(delayed, new MemoryStore());
        await model.InitializeAsync();
        var position = model.Position;
        var selection = model.SelectedVerse?.Id;
        var enteringMushaf = model.UpdateSettingsAsync(model.Settings with { Mode = ReadingMode.Mushaf });
        await delayed.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await model.UpdateSettingsAsync(model.Settings with { Mode = ReadingMode.Tafsir });
        delayed.Release.SetResult();
        await enteringMushaf;
        Assert.Equal(ReadingMode.Tafsir, model.Settings.Mode);
        Assert.Empty(model.FacingVerses);
        Assert.Equal(position, model.Position);
        Assert.Equal(selection, model.SelectedVerse?.Id);
        Assert.All(model.Verses, v => Assert.Equal(model.Page, v.Page));
    }

    private sealed class MemoryStore : IUserStateStore
    {
        public Task<UserState> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(UserState.Default);
        public Task SaveAsync(UserState state, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class DelayedFacingPage(IContentRepository source) : IContentRepository
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task ValidateAsync(CancellationToken cancellationToken = default) => source.ValidateAsync(cancellationToken);
        public Task<IReadOnlyList<Surah>> GetSurahsAsync(CancellationToken cancellationToken = default) => source.GetSurahsAsync(cancellationToken);
        public Task<int?> FindPageAsync(AyahId id, CancellationToken cancellationToken = default) => source.FindPageAsync(id, cancellationToken);
        public Task<string> GetTafsirAsync(AyahId id, CancellationToken cancellationToken = default) => source.GetTafsirAsync(id, cancellationToken);
        public async Task<IReadOnlyList<Verse>> GetPageAsync(int page, CancellationToken cancellationToken = default)
        {
            if (page == 2)
            {
                Started.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return await source.GetPageAsync(page, cancellationToken);
        }
    }
}
