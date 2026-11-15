using QuranReader.Core;
using QuranReader.Data;

namespace QuranReader.Tests;

public sealed class StabilityRegressionTests
{
    [Fact]
    public void EstimatePageLayout_WithEmptyVerses_ReturnsSafeEstimateWithoutThrowing()
    {
        var estimate = ReadingLayout.EstimatePageLayout([], 600, 800);
        Assert.NotNull(estimate);
        Assert.True(estimate.Scale >= ReadingLayout.MinComfortableScale);
        Assert.True(estimate.FitsWithoutScroll);
    }

    [Fact]
    public void CalculateSpreadScale_WithEmptyFacingOrPrimary_ReturnsSafeScaleWithoutThrowing()
    {
        var (scale1, fits1) = ReadingLayout.CalculateSpreadScale([], [], 600, 800);
        Assert.True(scale1 >= ReadingLayout.MinComfortableScale);

        IReadOnlyList<Verse> sample = [new(new(1, 1), "الفاتحة", 1, 1, "بِسْمِ اللَّهِ الرَّحْمَٰنِ الرَّحِيمِ")];
        var (scale2, fits2) = ReadingLayout.CalculateSpreadScale(sample, [], 600, 800);
        Assert.True(scale2 >= ReadingLayout.MinComfortableScale);

        var (scale3, fits3) = ReadingLayout.CalculateSpreadScale([], sample, 600, 800);
        Assert.True(scale3 >= ReadingLayout.MinComfortableScale);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(42)]
    [InlineData(604)]
    public async Task ReaderViewModel_NavigationAcrossKeyPages_MaintainsStateIntegrity(int page)
    {
        var store = new MemoryStore();
        var content = new MockContent();
        using var model = new ReaderViewModel(content, store);
        await model.InitializeAsync();

        await model.NavigateAsync(page);

        Assert.Equal(page, model.Page);
        Assert.NotEmpty(model.Verses);
        Assert.NotNull(model.SelectedVerse);
        Assert.Equal(page, model.SelectedVerse.Page);
        Assert.Null(model.Error);
    }

    [Fact]
    public async Task ReaderViewModel_ThirtyRepeatedModeSwitches_PreserveAnchorAndSelection()
    {
        var store = new MemoryStore();
        var content = new MockContent();
        using var model = new ReaderViewModel(content, store);
        await model.InitializeAsync();

        var initialVerse = model.SelectedVerse;
        Assert.NotNull(initialVerse);

        for (var i = 0; i < 30; i++)
        {
            var targetMode = i % 2 == 0 ? ReadingMode.Mushaf : ReadingMode.Tafsir;
            await model.UpdateSettingsAsync(model.Settings with { Mode = targetMode });

            Assert.Equal(targetMode, model.Settings.Mode);
            Assert.NotNull(model.SelectedVerse);
            Assert.Equal(initialVerse.Id, model.SelectedVerse.Id);
            Assert.NotEmpty(model.Verses);
            Assert.Null(model.Error);
        }
    }

    [Fact]
    public async Task ReaderViewModel_RapidOverlappingNavigation_RejectsSupersededResults()
    {
        var store = new MemoryStore();
        var content = new MockContent();
        using var model = new ReaderViewModel(content, store);
        await model.InitializeAsync();

        // Fire multiple navigations without awaiting
        var nav1 = model.NavigateAsync(2);
        var nav2 = model.NavigateAsync(42);
        var nav3 = model.NavigateAsync(604);

        await Task.WhenAll(nav1, nav2, nav3);

        // Final settled page must be page 604
        Assert.Equal(604, model.Page);
        Assert.Equal(604, model.SelectedVerse?.Page);
        Assert.NotEmpty(model.Verses);
        Assert.Null(model.Error);
    }

    [Fact]
    public async Task ReaderViewModel_TafsirCommentaryRemainsSynchronizedWithSelectedAyah()
    {
        var store = new MemoryStore();
        var content = new MockContent();
        using var model = new ReaderViewModel(content, store);
        await model.InitializeAsync();
        await model.UpdateSettingsAsync(model.Settings with { TafsirOpen = true });

        // Navigate to page 42 and select Ayah 2:255
        await model.NavigateAsync(42);
        var verse255 = model.Verses.First(v => v.Id == new AyahId(2, 255));
        await model.SelectAsync(verse255);

        Assert.Equal(new AyahId(2, 255), model.SelectedVerse?.Id);
        Assert.Contains("2:255", model.Tafsir);

        // Select next verse 2:256
        var verse256 = model.Verses.First(v => v.Id == new AyahId(2, 256));
        await model.SelectAsync(verse256);

        Assert.Equal(new AyahId(2, 256), model.SelectedVerse?.Id);
        Assert.Contains("2:256", model.Tafsir);
    }

    private sealed class MemoryStore : IUserStateStore
    {
        public UserState State { get; set; } = UserState.Default;
        public Task<UserState> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(State);
        public Task SaveAsync(UserState state, CancellationToken cancellationToken = default)
        {
            State = state;
            return Task.CompletedTask;
        }
    }

    private sealed class MockContent : IContentRepository
    {
        public Task ValidateAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<Surah>> GetSurahsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Surah>>([
                new(1, "الفاتحة", "Al-Fatihah", 1),
                new(2, "البقرة", "Al-Baqarah", 2),
                new(112, "الإخلاص", "Al-Ikhlas", 604)
            ]);

        public Task<IReadOnlyList<Verse>> GetPageAsync(int page, CancellationToken cancellationToken = default)
        {
            var list = new List<Verse>();
            if (page == 1)
            {
                list.Add(new(new(1, 1), "الفاتحة", 1, 1, "بِسْمِ اللَّهِ الرَّحْمَٰنِ الرَّحِيمِ"));
                list.Add(new(new(1, 2), "الفاتحة", 1, 1, "الْحَمْدُ لِلَّهِ رَبِّ الْعَالَمِينَ"));
            }
            else if (page == 2)
            {
                list.Add(new(new(2, 1), "البقرة", 2, 1, "الم"));
                list.Add(new(new(2, 2), "البقرة", 2, 1, "ذَٰلِكَ الْكِتَابُ لَا رَيْبَ ۛ فِيهِ"));
            }
            else if (page == 42)
            {
                list.Add(new(new(2, 255), "البقرة", 42, 3, "اللَّهُ لَا إِلَٰهَ إِلَّا هُوَ الْحَيُّ الْقَيُّومُ"));
                list.Add(new(new(2, 256), "البقرة", 42, 3, "لَا إِكْرَاهَ فِي الدِّينِ"));
            }
            else
            {
                list.Add(new(new(112, 1), "الإخلاص", 604, 30, "قُلْ هُوَ اللَّهُ أَحَدٌ"));
                list.Add(new(new(114, 6), "الناس", 604, 30, "مِنَ الْجِنَّةِ وَالنَّاسِ"));
            }
            return Task.FromResult<IReadOnlyList<Verse>>(list);
        }

        public Task<int?> FindPageAsync(AyahId id, CancellationToken cancellationToken = default) =>
            Task.FromResult<int?>(id.Surah == 2 && id.Ayah >= 255 ? 42 : id.Surah == 112 ? 604 : id.Surah);

        public Task<string> GetTafsirAsync(AyahId id, CancellationToken cancellationToken = default) =>
            Task.FromResult($"commentary for {id.Surah}:{id.Ayah}");
    }
}
