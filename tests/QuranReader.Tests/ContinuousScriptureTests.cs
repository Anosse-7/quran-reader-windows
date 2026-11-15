using System.Globalization;
using System.Security.Cryptography;
using QuranReader.Core;
using QuranReader.Data;

namespace QuranReader.Tests;

public sealed class ContinuousScriptureTests
{
    private static string ContentPath => Path.Combine(AppContext.BaseDirectory, "Content", "quran_reader_candidate.sqlite3");
    private static ContentRepository Repository() => new(ContentPath);

    [Fact]
    public async Task SourceWindowCrossesPublisherPagesInExactCanonicalOrder()
    {
        using var content = Repository();
        await content.ValidateAsync();
        var source = await content.GetSequenceAsync(new(2, 6), ContinuousScripture.MaxVerses);
        var document = new ContinuousScripture(source, new(new(2, 6)));
        Assert.True(document.Spans.Select(s => s.Source.Page).Distinct().Count() > 2);
        Assert.Equal(string.Concat(source.Take(document.Spans.Count).Select(v => v.Text)), string.Concat(document.Spans.Select(s => s.Text)));
        Assert.All(document.Spans, span => { Assert.Equal(0, span.Start); Assert.Equal(span.Source.Text, span.Text); });
        Assert.InRange(document.CodeUnits, 1, ContinuousScripture.MaxCodeUnits);
        Assert.Equal(new AyahId(2, 6), document.Start.Ayah);
        Assert.Equal(ContentRepository.PinnedSha256, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(ContentPath))).ToLowerInvariant());
    }

    [Fact]
    public async Task EveryLongAyahCodeUnitSurvivesMultipleMeasuredPartitions()
    {
        using var content = Repository();
        await content.ValidateAsync();
        var source = await content.GetSequenceAsync(new(2, 282), 1);
        var verse = source.Single();
        var boundaries = StringInfo.ParseCombiningCharacters(verse.Text);
        var cut1 = boundaries[boundaries.Length / 3];
        var cut2 = boundaries[boundaries.Length * 2 / 3];
        var document = new ContinuousScripture(source, new(verse.Id));
        var first = document.Between(document.Start, new(verse.Id, TextOffset: cut1));
        var second = document.Between(new(verse.Id, TextOffset: cut1), new(verse.Id, TextOffset: cut2));
        var third = document.Between(new(verse.Id, TextOffset: cut2), document.End);
        var pieces = first.Concat(second).Concat(third).ToArray();
        Assert.Equal(verse.Text, string.Concat(pieces.Select(s => s.Text)));
        Assert.Equal(Enumerable.Range(0, verse.Text.Length), pieces.SelectMany(s => Enumerable.Range(s.Start, s.Length)));
        Assert.All(pieces, part => Assert.Equal(verse.Id, part.Source.Id));
        // Copy uses the authoritative whole source, including its publisher marker.
        Assert.All(pieces, part => Assert.Same(verse.Text, part.Source.Text));
    }

    [Fact]
    public async Task GraphemeInteriorOffsetsAreRejectedRatherThanRepaired()
    {
        using var content = Repository();
        await content.ValidateAsync();
        var source = await content.GetSequenceAsync(new(2, 255), 1);
        var verse = source[0];
        var invalid = Enumerable.Range(1, verse.Text.Length - 1).First(n => !ContinuousScripture.IsBoundary(verse.Text, n));
        Assert.Throws<ResourceException>(() => new ContinuousScripture(source, new(verse.Id, TextOffset: invalid)));
        var document = new ContinuousScripture(source, new(verse.Id));
        Assert.Throws<ResourceException>(() => document.Between(document.Start, new(verse.Id, TextOffset: invalid)));
        Assert.Throws<ResourceException>(() => ContinuousScripture.RequireBoundary(verse.Text, -1));
        Assert.Throws<ResourceException>(() => ContinuousScripture.RequireBoundary(verse.Text, verse.Text.Length + 1));
    }

    [Fact]
    public async Task MissingRepeatedAndOutOfOrderAyatFailBeforeComposition()
    {
        using var content = Repository();
        await content.ValidateAsync();
        var verses = await content.GetSequenceAsync(new(2, 6), 4);
        Assert.Throws<ResourceException>(() => new ContinuousScripture([verses[0], verses[2]], new(verses[0].Id)));
        Assert.Throws<ResourceException>(() => new ContinuousScripture([verses[0], verses[0]], new(verses[0].Id)));
        Assert.Throws<ResourceException>(() => new ContinuousScripture([verses[1], verses[0]], new(verses[1].Id)));
    }

    [Theory]
    [InlineData(8, 75, 9, 1)]
    [InlineData(111, 5, 112, 1)]
    [InlineData(112, 4, 113, 1)]
    public async Task SurahTransitionsKeepWholeAyatAndStableIdentities(int a, int b, int c, int d)
    {
        using var content = Repository();
        await content.ValidateAsync();
        var source = await content.GetSequenceAsync(new(a, b), 4);
        var document = new ContinuousScripture(source, new(new(a, b)));
        Assert.Equal(new AyahId(c, d), document.Spans[1].Source.Id);
        Assert.Equal(source.Select(v => v.Text), document.Spans.Select(s => s.Text));
        Assert.Equal(c != 1 && c != 9, SurahMetadata.HasBasmalahHeader(c));
    }

    [Fact]
    public async Task ContinuousSelectionReusesTafsirBookmarksAndIndependentAnchor()
    {
        using var content = Repository();
        var store = new MemoryState();
        using var model = new ReaderViewModel(content, store);
        await model.InitializeAsync();
        await model.GoToAsync(new(2, 6));
        await model.UpdateSettingsAsync(model.Settings with { Mode = ReadingMode.AdaptiveBook, Scale = 28 });
        var document = model.Continuous!;
        var anchor = model.Position;
        var selected = document.Spans.First(s => s.Source.Page >= 6).Source;
        await model.SelectAsync(selected);
        Assert.Same(document, model.Continuous);
        Assert.Equal(anchor, model.Position);
        Assert.Equal(selected.Id, model.SelectedVerse?.Id);
        Assert.Equal(await content.GetTafsirAsync(selected.Id), model.Tafsir);
        await model.ToggleBookmarkAsync();
        Assert.Contains(selected.Id, store.State.Bookmarks);
        await model.UpdateSettingsAsync(model.Settings with { Scale = 18, Theme = ReadingTheme.Dark, DrawerWidth = 560 });
        Assert.Same(document, model.Continuous);
        Assert.Equal(selected.Id, model.SelectedVerse?.Id);
        Assert.Equal(anchor, model.Position);
        Assert.Equal(18, model.Settings.Scale);
    }

    [Fact]
    public async Task PreviewCursorSelectionAndPreferencesRestoreThroughExistingStore()
    {
        var folder = Path.Combine(Path.GetTempPath(), "QuranReader.Tests", Guid.NewGuid().ToString());
        try
        {
            using var content = Repository();
            await content.ValidateAsync();
            var verse = (await content.GetSequenceAsync(new(2, 282), 1))[0];
            var offset = StringInfo.ParseCombiningCharacters(verse.Text)[80];
            var position = new ReadingPosition(verse.Id, 0, offset);
            var path = Path.Combine(folder, "preferences.db");
            var expected = new UserState(position, new(ReadingTheme.Sepia, 32, Mode: ReadingMode.AdaptiveBook), [new(2, 255)], new(2, 6));
            await new UserStateStore(path).SaveAsync(expected);
            using var model = new ReaderViewModel(content, new UserStateStore(path));
            await model.InitializeAsync();
            Assert.True(model.Ready, model.Error);
            Assert.Equal(position, model.Position);
            Assert.Equal(new AyahId(2, 6), model.SelectedVerse?.Id);
            Assert.Equal(expected.Settings, model.Settings);
            Assert.Equal(offset, model.Continuous!.Spans[0].Start);
            Assert.Contains(new AyahId(2, 255), model.Bookmarks);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task LatestContinuousNavigationWinsEvenWhenProviderIgnoresCancellation()
    {
        using var actual = Repository();
        var delayed = new DelayedSequence(actual);
        using var model = new ReaderViewModel(delayed, new MemoryState());
        await model.InitializeAsync();
        await model.UpdateSettingsAsync(model.Settings with { Mode = ReadingMode.AdaptiveBook });
        delayed.Delay = true;
        var stale = model.LoadContinuousWindowAsync(new(new(2, 6)));
        await delayed.Started.Task;
        await model.GoToAsync(new(2, 255));
        delayed.Release.SetResult();
        await stale;
        Assert.Equal(new AyahId(2, 255), model.Continuous!.Start.Ayah);
        Assert.Equal(new AyahId(2, 255), model.Position.Ayah);
        Assert.False(model.IsBusy);
    }

    [Fact]
    public async Task NativeMeasuredBreakContractRejectsCorruptOrDuplicatedCursors()
    {
        using var content = Repository();
        await content.ValidateAsync();
        var source = await content.GetSequenceAsync(new(2, 255), 4);
        var document = new ContinuousScripture(source, new(source[0].Id));
        var second = new ReadingPosition(source[1].Id);
        var leaves = ContinuousPagination.FromMeasuredBreaks(document, [document.Start, second], document.End);
        Assert.Equal(string.Concat(document.Spans.Select(s => s.Text)), string.Concat(leaves.SelectMany(l => l.Spans).Select(s => s.Text)));
        Assert.Throws<ResourceException>(() => ContinuousPagination.FromMeasuredBreaks(document, [document.Start, document.Start], document.End));
        Assert.Throws<ResourceException>(() => ContinuousPagination.FromMeasuredBreaks(document, [second], document.End));
        Assert.Throws<ResourceException>(() => ContinuousPagination.FromMeasuredBreaks(document, [document.Start, new(new(2, 999))], document.End));
        var invalid = Enumerable.Range(1, source[0].Text.Length - 1).First(i => !ContinuousScripture.IsBoundary(source[0].Text, i));
        Assert.Throws<ResourceException>(() => ContinuousPagination.FromMeasuredBreaks(document, [document.Start, new(source[0].Id, TextOffset: invalid)], document.End));
    }

    [Fact]
    public async Task ExactSourceReadDoesNotRepositionTheReaderOrInventBackwardContext()
    {
        using var content = Repository();
        using var model = new ReaderViewModel(content, new MemoryState());
        await model.InitializeAsync();
        await model.GoToAsync(new(2, 255));
        await model.UpdateSettingsAsync(model.Settings with { Mode = ReadingMode.AdaptiveBook });
        var anchor = model.Position;
        var current = model.Continuous;
        var source = await model.ReadContinuousSourceAsync(anchor);
        Assert.Equal(anchor, source.Start);
        Assert.True(ContinuousPagination.Compare(source.End, anchor) > 0);
        Assert.Equal(anchor, model.Position);
        Assert.Same(current, model.Continuous);
        await model.UpdateSettingsAsync(model.Settings with { Mode = ReadingMode.Mushaf });
        Assert.Null(model.Continuous);
        Assert.Equal(anchor, model.Position);
        Assert.Equal(ReadingMode.Mushaf, model.Settings.Mode);
    }

    [Fact]
    public async Task OffscreenCommentarySelectionDoesNotRelabelTheVisibleCanonicalPage()
    {
        using var content = Repository();
        using var model = new ReaderViewModel(content, new MemoryState());
        await model.InitializeAsync();
        await model.GoToAsync(new(2, 6));
        await model.UpdateSettingsAsync(model.Settings with { Mode = ReadingMode.AdaptiveBook });
        var selected = model.Continuous!.Spans.First(s => s.Source.Page >= 10).Source;
        await model.SelectAsync(selected);
        var anchor = model.Position;
        await model.UpdateSettingsAsync(model.Settings with { Mode = ReadingMode.Mushaf });
        Assert.Equal(anchor.Ayah, model.Position.Ayah);
        Assert.Equal(selected.Id, model.SelectedVerse!.Id);
        Assert.Equal(3, model.ActivePage);
        Assert.False(model.CanonicalSelectionLoaded);
    }

    [Fact]
    public async Task NewNavigationRejectsPositionSavesFromRetiredPresentedScripture()
    {
        using var content = Repository();
        using var model = new ReaderViewModel(content, new MemoryState());
        await model.InitializeAsync();
        await model.UpdateSettingsAsync(model.Settings with { Mode = ReadingMode.AdaptiveBook });
        var retired = await model.ReadContinuousSourceAsync(new(new(111, 1)));
        model.BindPresentedContinuous(retired);
        await model.RememberPositionAsync(new(111, 1), 0);
        Assert.Equal(new AyahId(111, 1), model.Position.Ayah);
        await model.GoToAsync(new(1, 1));
        await model.RememberPositionAsync(new(111, 1), 0);
        Assert.Equal(new AyahId(1, 1), model.Position.Ayah);
        Assert.Equal(1, model.Page);
        Assert.Equal(1, model.Verses[0].Page);
    }

    [Fact]
    public async Task UnchangedSettingsDoNotSaveNotifyOrDisturbActiveReadingState()
    {
        using var content = Repository();
        var store = new MemoryState();
        using var model = new ReaderViewModel(content, store);
        await model.InitializeAsync();
        await model.GoToAsync(new(2, 255));
        await model.UpdateSettingsAsync(model.Settings with { Mode = ReadingMode.AdaptiveBook });
        var position = model.Position;
        var selected = model.SelectedVerse;
        var scripture = model.Continuous;
        var tafsir = model.Tafsir;
        var saves = store.SaveCount;
        var notifications = 0;
        model.PropertyChanged += (_, _) => notifications++;
        await model.UpdateSettingsAsync(model.Settings);
        await model.UpdateSettingsAsync(model.Settings with { Scale = double.NaN });
        Assert.Equal(saves, store.SaveCount);
        Assert.Equal(0, notifications);
        Assert.Equal(position, model.Position);
        Assert.Same(selected, model.SelectedVerse);
        Assert.Same(scripture, model.Continuous);
        Assert.Equal(tafsir, model.Tafsir);
    }

    [Fact]
    public async Task EnteringPreviewAtOpeningLoadsTheFacingOpeningPage()
    {
        using var content = Repository();
        using var model = new ReaderViewModel(content, new MemoryState());
        await model.InitializeAsync();
        await model.UpdateSettingsAsync(model.Settings with { Mode = ReadingMode.AdaptiveBook });
        Assert.Equal(1, model.Verses[0].Page);
        Assert.Equal(2, model.FacingVerses[0].Page);
    }

    [Fact]
    public async Task CanonicalOrdinaryOriginDoesNotReflowSpecialOpeningAyat()
    {
        using var content = Repository();
        using var model = new ReaderViewModel(content, new MemoryState());
        await model.InitializeAsync();
        await model.GoToAsync(new(2, 7));
        await model.UpdateSettingsAsync(model.Settings with { Mode = ReadingMode.AdaptiveBook });
        var source = await model.ReadContinuousSourceAsync(AdaptivePaginationIndex.Origin);
        Assert.Equal(new ReadingPosition(new(2, 6)), source.Start);
        Assert.All(source.Spans, span => Assert.True(ContinuousScripture.Compare(span.Source.Id, new(2, 6)) >= 0));
    }

    private sealed class MemoryState : IUserStateStore
    {
        public int SaveCount { get; private set; }
        public UserState State { get; private set; } = UserState.Default;
        public Task<UserState> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(State);
        public Task SaveAsync(UserState state, CancellationToken cancellationToken = default) { SaveCount++; State = state; return Task.CompletedTask; }
    }

    private sealed class DelayedSequence(IContinuousContentRepository inner) : IContinuousContentRepository
    {
        public bool Delay { get; set; }
        public TaskCompletionSource Started { get; } = new();
        public TaskCompletionSource Release { get; } = new();
        public async Task<IReadOnlyList<Verse>> GetSequenceAsync(AyahId start, int limit, CancellationToken cancellationToken = default)
        {
            if (Delay && start == new AyahId(2, 6)) { Started.TrySetResult(); await Release.Task; }
            return await inner.GetSequenceAsync(start, limit); // deliberately ignores caller cancellation
        }
        public Task<IReadOnlyList<Verse>> GetPrecedingAsync(AyahId before, int limit, CancellationToken cancellationToken = default) => inner.GetPrecedingAsync(before, limit, cancellationToken);
        public Task ValidateAsync(CancellationToken cancellationToken = default) => inner.ValidateAsync(cancellationToken);
        public Task<IReadOnlyList<Surah>> GetSurahsAsync(CancellationToken cancellationToken = default) => inner.GetSurahsAsync(cancellationToken);
        public Task<IReadOnlyList<Verse>> GetPageAsync(int page, CancellationToken cancellationToken = default) => inner.GetPageAsync(page, cancellationToken);
        public Task<int?> FindPageAsync(AyahId id, CancellationToken cancellationToken = default) => inner.FindPageAsync(id, cancellationToken);
        public Task<string> GetTafsirAsync(AyahId id, CancellationToken cancellationToken = default) => inner.GetTafsirAsync(id, cancellationToken);
    }
}
