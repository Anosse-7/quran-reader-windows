using QuranReader.Core;
using QuranReader.Data;

namespace QuranReader.App;

internal sealed class TracedContentRepository(ContentRepository inner) : IContinuousContentRepository
{
    private static async Task<T> Read<T>(string phase, Func<Task<T>> query)
    {
        var trace = NavigationTrace.Current;
        NavigationTrace.Mark(trace, phase + "Started");
        try { return await query(); }
        finally { NavigationTrace.Mark(trace, phase + "Completed"); }
    }
    public Task ValidateAsync(CancellationToken cancellationToken = default) => inner.ValidateAsync(cancellationToken);
    public Task<IReadOnlyList<Surah>> GetSurahsAsync(CancellationToken cancellationToken = default) => Read("ContentSurahs", () => inner.GetSurahsAsync(cancellationToken));
    public Task<IReadOnlyList<Verse>> GetPageAsync(int page, CancellationToken cancellationToken = default) => Read("ContentPage", () => inner.GetPageAsync(page, cancellationToken));
    public Task<int?> FindPageAsync(AyahId id, CancellationToken cancellationToken = default) => Read("ContentPageLookup", () => inner.FindPageAsync(id, cancellationToken));
    public Task<string> GetTafsirAsync(AyahId id, CancellationToken cancellationToken = default) => Read("ContentTafsir", () => inner.GetTafsirAsync(id, cancellationToken));
    public Task<IReadOnlyList<Verse>> GetSequenceAsync(AyahId start, int limit, CancellationToken cancellationToken = default) => Read("ContentSequence", () => inner.GetSequenceAsync(start, limit, cancellationToken));
    public Task<IReadOnlyList<Verse>> GetPrecedingAsync(AyahId before, int limit, CancellationToken cancellationToken = default) => Read("ContentPreceding", () => inner.GetPrecedingAsync(before, limit, cancellationToken));
}

internal sealed class TracedUserStateStore(IUserStateStore inner) : IUserStateStore
{
    public Task<UserState> LoadAsync(CancellationToken cancellationToken = default) => inner.LoadAsync(cancellationToken);
    public async Task SaveAsync(UserState state, CancellationToken cancellationToken = default)
    {
        var trace = NavigationTrace.Current;
        NavigationTrace.Mark(trace, "PersistenceStarted");
        try { await inner.SaveAsync(state, cancellationToken); }
        finally { NavigationTrace.Mark(trace, "PersistenceCompleted"); }
    }
}
