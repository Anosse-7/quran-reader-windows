using QuranReader.Core;

namespace QuranReader.Tests;

public sealed class ReaderTests
{
    [Fact]
    public async Task RestoreNavigateSelectTafsirAndBookmarks()
    {
        var store = new MemoryStore { State = new(new(new(2, 1), -18), new(ReadingTheme.Dark, 36), [new(2, 1)]) };
        using var model = new ReaderViewModel(new FakeContent(), store);
        await model.InitializeAsync();
        Assert.True(model.Ready); Assert.Equal(2, model.Page); Assert.Equal(-18, model.Position.Offset);
        Assert.True(model.IsBookmarked);
        await model.UpdateSettingsAsync(model.Settings with { TafsirOpen = true, Scale = 500 });
        Assert.Equal(56, model.Settings.Scale); Assert.Equal("commentary 2:1", model.Tafsir);
        await model.ToggleBookmarkAsync(); Assert.False(model.IsBookmarked);
        await model.RememberPositionAsync(new(2, 1), -24);
        await model.NavigateAsync(3); Assert.Equal(3, model.Page); Assert.Equal("commentary 3:1", model.Tafsir);
        await model.NavigateAsync(605); Assert.Equal(3, model.Page);
        await model.NavigateAsync(0); Assert.Equal(3, model.Page);
        await model.GoToAsync(new(4, 1)); Assert.Equal(4, model.Page);
        await model.SelectAsync(model.Verses[0]);
        Assert.Equal(store.State.Position, model.Position);
    }

    [Fact]
    public async Task InvalidContentDoesNotEnableReader()
    {
        using var model = new ReaderViewModel(new FakeContent { FailValidation = true }, new MemoryStore());
        await model.InitializeAsync();
        Assert.False(model.Ready); Assert.NotNull(model.Error); Assert.Empty(model.Verses);
    }

    [Fact]
    public async Task LatestNavigationWinsWhenEarlierRequestFinishesLate()
    {
        var content = new FakeContent();
        using var model = new ReaderViewModel(content, new MemoryStore());
        await model.InitializeAsync();
        content.DelayedPage = 2;
        var slow = model.NavigateAsync(2);
        await model.NavigateAsync(3);
        content.ReleaseDelay.SetResult();
        await slow;
        Assert.Equal(3, model.Page); Assert.Equal(new AyahId(3, 1), model.SelectedVerse!.Id);
        Assert.False(model.IsBusy);
    }

    [Fact]
    public async Task SaveFailuresRemainVisible()
    {
        using var model = new ReaderViewModel(new FakeContent(), new MemoryStore { FailSave = true });
        await model.InitializeAsync();
        Assert.Contains("save failure", model.Error);
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<img src='https://example.invalid' onerror='run()'>")]
    [InlineData("<span class='other'>literal</span>")]
    [InlineData("<span class='aya' onclick='run()'>literal</span>")]
    public void UnsafeTafsirMarkupRemainsInertLiteralText(string text)
    {
        var spans = TafsirFormatter.Parse(text);
        Assert.Equal(text, string.Concat(spans.Select(s => s.Text)));
        Assert.All(spans, span => Assert.False(span.IsAyah));
    }

    [Fact]
    public void OnlyAllowlistedTafsirSpanIsStyled()
    {
        var spans = TafsirFormatter.Parse("before <span class='aya'>sample &amp; text</span> after");
        Assert.Equal(3, spans.Count); Assert.True(spans[1].IsAyah); Assert.Equal("sample & text", spans[1].Text);
    }

    private sealed class MemoryStore : IUserStateStore
    {
        public UserState State { get; set; } = UserState.Default;
        public bool FailSave { get; init; }
        public Task<UserState> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(State);
        public Task SaveAsync(UserState state, CancellationToken cancellationToken = default)
        {
            if (FailSave) throw new ResourceException("save failure");
            State = state; return Task.CompletedTask;
        }
    }

    private sealed class FakeContent : IContentRepository
    {
        public bool FailValidation { get; init; }
        public int DelayedPage { get; set; }
        public TaskCompletionSource ReleaseDelay { get; } = new();
        public Task ValidateAsync(CancellationToken cancellationToken = default) => FailValidation ? Task.FromException(new ResourceException("bad hash")) : Task.CompletedTask;
        public Task<IReadOnlyList<Surah>> GetSurahsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Surah>>([new(1, "", "Test", 1)]);
        public async Task<IReadOnlyList<Verse>> GetPageAsync(int page, CancellationToken cancellationToken = default)
        {
            if (page == DelayedPage) await ReleaseDelay.Task;
            return [new(new(page, 1), "", page, 1, "synthetic test fixture")];
        }
        public Task<int?> FindPageAsync(AyahId id, CancellationToken cancellationToken = default) => Task.FromResult<int?>(id.Surah);
        public Task<string> GetTafsirAsync(AyahId id, CancellationToken cancellationToken = default) => Task.FromResult($"commentary {id}");
    }
}
