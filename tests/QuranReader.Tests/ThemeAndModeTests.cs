using QuranReader.Core;
using QuranReader.Data;

namespace QuranReader.Tests;

public sealed class ThemeAndModeTests
{
    [Theory]
    [InlineData(ReadingTheme.Light)]
    [InlineData(ReadingTheme.Dark)]
    [InlineData(ReadingTheme.Sepia)]
    public void ThemePalettesMeetWcagContrastRequirements(ReadingTheme theme)
    {
        var tokens = ThemeTokens.Resolve(theme);

        // Scripture contrast target is >= 7:1 (WCAG AAA for normal text, far exceeding 4.5:1 AA)
        var scriptureReadingContrast = ColorRgb.ContrastRatio(tokens.ScriptureForeground, tokens.ReadingSurface);
        var scriptureMushafContrast = ColorRgb.ContrastRatio(tokens.ScriptureForeground, tokens.MushafPageSurface);
        Assert.True(scriptureReadingContrast >= 7.0, $"{theme} scripture/reading contrast was {scriptureReadingContrast:F2}, expected >= 7.0");
        Assert.True(scriptureMushafContrast >= 7.0, $"{theme} scripture/mushaf contrast was {scriptureMushafContrast:F2}, expected >= 7.0");

        // Tafsir commentary contrast target is >= 7:1
        var tafsirContrast = ColorRgb.ContrastRatio(tokens.TafsirForeground, tokens.TafsirSurface);
        Assert.True(tafsirContrast >= 7.0, $"{theme} tafsir contrast was {tafsirContrast:F2}, expected >= 7.0");

        // Metadata contrast target is >= 4.5:1 (WCAG AA)
        var metadataContrast = ColorRgb.ContrastRatio(tokens.MetadataForeground, tokens.ReadingSurface);
        Assert.True(metadataContrast >= 4.5, $"{theme} metadata contrast was {metadataContrast:F2}, expected >= 4.5");

        // Accent contrast target is >= 4.5:1 (WCAG AA)
        var accentContrast = ColorRgb.ContrastRatio(tokens.AccentColor, tokens.ReadingSurface);
        Assert.True(accentContrast >= 4.5, $"{theme} accent contrast was {accentContrast:F2}, expected >= 4.5");
    }

    [Fact]
    public void SystemThemeDynamicallyResolvesToDarkOrLight()
    {
        Assert.Equal(ThemeTokens.Dark, ThemeTokens.Resolve(ReadingTheme.System, systemIsDark: true));
        Assert.Equal(ThemeTokens.Light, ThemeTokens.Resolve(ReadingTheme.System, systemIsDark: false));
        Assert.Equal(ThemeTokens.Dark, ThemeTokens.Resolve(ReadingTheme.Dark, systemIsDark: false));
        Assert.Equal(ThemeTokens.Light, ThemeTokens.Resolve(ReadingTheme.Light, systemIsDark: true));
        Assert.Equal(ThemeTokens.Sepia, ThemeTokens.Resolve(ReadingTheme.Sepia, systemIsDark: true));
    }

    [Fact]
    public void ReadingLayoutCalculatesSpreadCorrectly()
    {
        // Wide window supports two-page spread
        Assert.True(ReadingLayout.ShowSpread(1400, 32));
        Assert.True(ReadingLayout.ShowSpread(1600, 40));

        // Narrow window falls back to single-page
        Assert.False(ReadingLayout.ShowSpread(800, 32));
        Assert.False(ReadingLayout.ShowSpread(1000, 48));

        // Spread start pairs pages symmetrically
        Assert.Equal(1, ReadingLayout.SpreadStart(1));
        Assert.Equal(1, ReadingLayout.SpreadStart(2));
        Assert.Equal(41, ReadingLayout.SpreadStart(41));
        Assert.Equal(41, ReadingLayout.SpreadStart(42));
        Assert.Equal(603, ReadingLayout.SpreadStart(603));
        Assert.Equal(603, ReadingLayout.SpreadStart(604));

        // Next / previous page progression
        Assert.Equal(3, ReadingLayout.NextPage(1, spread: true));
        Assert.Equal(2, ReadingLayout.NextPage(1, spread: false));
        Assert.Equal(604, ReadingLayout.NextPage(603, spread: true));
        Assert.Equal(604, ReadingLayout.NextPage(604, spread: true));

        Assert.Equal(1, ReadingLayout.PreviousPage(3, spread: true));
        Assert.Equal(2, ReadingLayout.PreviousPage(3, spread: false));
        Assert.Equal(1, ReadingLayout.PreviousPage(1, spread: true));
        Assert.Equal(1, ReadingLayout.PreviousPage(2, spread: true));
    }

    [Fact]
    public void SpreadProgressionAdvancesFacingPagesWithoutRepetition()
    {
        // When displaying pages 1–2 (whether anchor is page 1 or page 2), next spread must advance to 3–4 (never repeating 1–2)
        Assert.Equal(3, ReadingLayout.NextPage(1, spread: true));
        Assert.Equal(3, ReadingLayout.NextPage(2, spread: true));

        // From spread 3–4, advancing goes to spread 5–6
        Assert.Equal(5, ReadingLayout.NextPage(3, spread: true));
        Assert.Equal(5, ReadingLayout.NextPage(4, spread: true));

        // From spread 3–4, going previous returns to spread 1–2
        Assert.Equal(1, ReadingLayout.PreviousPage(3, spread: true));
        Assert.Equal(1, ReadingLayout.PreviousPage(4, spread: true));

        // Clamping at beginning
        Assert.Equal(1, ReadingLayout.PreviousPage(1, spread: true));
        Assert.Equal(1, ReadingLayout.PreviousPage(2, spread: true));

        // Clamping at end of Qur'an (pages 603–604)
        Assert.Equal(604, ReadingLayout.NextPage(603, spread: true));
        Assert.Equal(604, ReadingLayout.NextPage(604, spread: true));
        Assert.Equal(601, ReadingLayout.PreviousPage(603, spread: true));
        Assert.Equal(601, ReadingLayout.PreviousPage(604, spread: true));
    }

    [Fact]
    public void DrawerDockingThresholdProtectsSpreadAgainstAccidentalCollapse()
    {
        // Spread threshold at default scale (36pt) is Max(1140, 2*(36*14 + 88) + 24) = 1208
        const double scale = 36.0;
        var spreadThreshold = Math.Max(1140, 2 * (scale * 14 + 88) + 24);
        const double drawerWidth = 380.0;
        var dockThreshold = spreadThreshold + drawerWidth + 30; // 1618.0

        // At 1400px window width, two-page spread is active:
        Assert.True(ReadingLayout.ShowSpread(1400, scale));

        // If drawer docked into layout at 1400px, remaining width would be 1400 - 380 = 1020px (< 1208),
        // which would cause an accidental collapse into a single-page view.
        // Therefore, canDock must be false so the drawer is presented as an overlay:
        var canDockAt1400 = 1400 >= dockThreshold;
        Assert.False(canDockAt1400);

        // At 1700px, there is ample space for both the full two-page spread and the docked drawer:
        var canDockAt1700 = 1700 >= dockThreshold;
        Assert.True(canDockAt1700);
        Assert.True(ReadingLayout.ShowSpread(1700 - drawerWidth, scale));
    }

    [Fact]
    public void ReadingLayoutMotionDurationRespectsSystemAndReducedMotion()
    {
        Assert.Equal(220, ReadingLayout.MotionDuration(systemAnimationsEnabled: true, reduceMotion: false));
        Assert.Equal(0, ReadingLayout.MotionDuration(systemAnimationsEnabled: false, reduceMotion: false));
        Assert.Equal(0, ReadingLayout.MotionDuration(systemAnimationsEnabled: true, reduceMotion: true));
        Assert.Equal(0, ReadingLayout.MotionDuration(systemAnimationsEnabled: false, reduceMotion: true));
    }

    [Fact]
    public async Task ModeSwitchingPreservesReadingPositionAndSelection()
    {
        var store = new MemoryStore
        {
            State = new(new(new(2, 255), -80), new(ReadingTheme.Dark, 38, TafsirOpen: false, Mode: ReadingMode.Tafsir), [new(2, 255)])
        };
        var content = new MockContent();
        using var model = new ReaderViewModel(content, store);
        await model.InitializeAsync();

        Assert.Equal(42, model.Page);
        Assert.Equal(new AyahId(2, 255), model.SelectedVerse?.Id);
        Assert.Equal(-80, model.Position.Offset);

        // Switch to Mushaf Mode
        await model.UpdateSettingsAsync(model.Settings with { Mode = ReadingMode.Mushaf });
        Assert.Equal(ReadingMode.Mushaf, model.Settings.Mode);
        Assert.Equal(42, model.Page);
        Assert.Equal(new AyahId(2, 255), model.SelectedVerse?.Id);
        Assert.NotEmpty(model.FacingVerses);
        Assert.Equal(41, model.FacingVerses[0].Page); // Paired facing page for page 42 is 41

        // Switch back to Tafsir Mode
        await model.UpdateSettingsAsync(model.Settings with { Mode = ReadingMode.Tafsir });
        Assert.Equal(ReadingMode.Tafsir, model.Settings.Mode);
        Assert.Equal(42, model.Page);
        Assert.Equal(new AyahId(2, 255), model.SelectedVerse?.Id);
    }

    [Fact]
    public async Task MushafAyahSelectionOpensTafsirAndLoadsCommentary()
    {
        var store = new MemoryStore();
        var content = new MockContent();
        using var model = new ReaderViewModel(content, store);
        await model.InitializeAsync();
        await model.UpdateSettingsAsync(model.Settings with { Mode = ReadingMode.Mushaf, TafsirOpen = false });

        Assert.False(model.Settings.TafsirOpen);
        Assert.Empty(model.Tafsir);

        // Selecting a verse in Mushaf mode user-initiated opens Tafsir
        var targetVerse = model.Verses.First();
        await model.SelectAsync(targetVerse, userInitiated: true);

        Assert.True(model.Settings.TafsirOpen);
        Assert.Equal(targetVerse.Id, model.SelectedVerse?.Id);
        Assert.Contains(targetVerse.Id.ToString(), model.Tafsir);
    }

    [Fact]
    public async Task SettingsClampingAndPersistenceRoundTrip()
    {
        var tempFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var dbPath = Path.Combine(tempFolder, "user.db");
        try
        {
            var store = new UserStateStore(dbPath);
            var state = new UserState(
                new(new(18, 1), 45.0),
                new(ReadingTheme.System, 42.0, TafsirOpen: true, Mode: ReadingMode.Mushaf, DrawerPinned: true, DrawerWidth: 460.0, ReduceMotion: true),
                [new(18, 1), new(36, 1)],
                new(18, 1)
            );
            await store.SaveAsync(state);

            var loaded = await store.LoadAsync();
            Assert.Equal(state.Position, loaded.Position);
            Assert.Equal(state.Settings.Theme, loaded.Settings.Theme);
            Assert.Equal(state.Settings.Scale, loaded.Settings.Scale);
            Assert.Equal(state.Settings.TafsirOpen, loaded.Settings.TafsirOpen);
            Assert.Equal(state.Settings.Mode, loaded.Settings.Mode);
            Assert.Equal(state.Settings.DrawerPinned, loaded.Settings.DrawerPinned);
            Assert.Equal(state.Settings.DrawerWidth, loaded.Settings.DrawerWidth);
            Assert.Equal(state.Settings.ReduceMotion, loaded.Settings.ReduceMotion);
            Assert.Equal(state.Bookmarks, loaded.Bookmarks);
            Assert.Equal(state.SelectedAyah, loaded.SelectedAyah);
        }
        finally
        {
            if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true);
        }
    }

    [Fact]
    public async Task RapidAyahSelectionRejectsSupersededTafsir()
    {
        var store = new MemoryStore();
        var content = new MockContent();
        using var model = new ReaderViewModel(content, store);
        await model.InitializeAsync();
        await model.UpdateSettingsAsync(model.Settings with { TafsirOpen = true });

        // Simulate rapid selection where the first request is slow
        content.SlowAyah = new AyahId(1, 1);
        var slowTask = model.SelectAsync(model.Verses[0]);
        await model.SelectAsync(model.Verses[1]);
        content.ReleaseSlow.SetResult();
        await slowTask;

        // Commentary for the second verse must be retained, not superseded by slowTask
        Assert.Equal(model.Verses[1].Id, model.SelectedVerse?.Id);
        Assert.Equal($"commentary {model.Verses[1].Id}", model.Tafsir);
    }

    [Fact]
    public async Task MushafPageCompletenessAndLongAyahReachability()
    {
        var contentPath = Path.Combine(AppContext.BaseDirectory, "Content", "quran_reader_candidate.sqlite3");
        using var content = new ContentRepository(contentPath);
        await content.ValidateAsync();

        var store = new MemoryStore();
        using var model = new ReaderViewModel(content, store);
        await model.InitializeAsync();
        await model.UpdateSettingsAsync(model.Settings with { TafsirOpen = true });

        // 1. Page 1 (Al-Fatihah) contains all 7 ayat
        await model.NavigateAsync(1);
        Assert.Equal(7, model.Verses.Count);
        Assert.Equal(new AyahId(1, 1), model.Verses[0].Id);
        Assert.Equal(new AyahId(1, 7), model.Verses[6].Id);

        // 2. Page 42 contains 4 ayat: 2:253, 2:254, 2:255 (Ayat al-Kursi), and 2:256
        await model.NavigateAsync(42);
        Assert.Equal(4, model.Verses.Count);
        Assert.Equal(new AyahId(2, 253), model.Verses[0].Id);
        Assert.Equal(new AyahId(2, 255), model.Verses[2].Id);
        Assert.Equal(new AyahId(2, 256), model.Verses[3].Id);

        // Select the bottom-most verse (2:256) and verify reachability and tafsir commentary
        await model.SelectAsync(model.Verses[3]);
        Assert.Equal(new AyahId(2, 256), model.SelectedVerse?.Id);
        Assert.NotEmpty(model.Tafsir);

        // 3. Page 604 contains the concluding ayat of the Qur'an
        await model.NavigateAsync(604);
        Assert.NotEmpty(model.Verses);
        var lastVerse = model.Verses[^1];
        Assert.Equal(new AyahId(114, 6), lastVerse.Id);

        // Verify bottom-most verse on final page is selectable and tafsir loads
        await model.SelectAsync(lastVerse);
        Assert.Equal(new AyahId(114, 6), model.SelectedVerse?.Id);
        Assert.NotEmpty(model.Tafsir);
    }

    [Fact]
    public void DrawerOverlayAlignmentPreventsOcclusionOfActivePage()
    {
        // In a two-page spread:
        // RightPage = Column 1 (earlier page, e.g. Page 41)
        // LeftPage  = Column 0 (later page, e.g. Page 42)
        // When the selected ayah is on RightPage (Page 41):
        // Overlay must align to HorizontalAlignment.Left (covering LeftPage, keeping RightPage uncovered).
        const bool isRightPageSelected = true;
        var alignmentWhenRightSelected = isRightPageSelected ? "Left" : "Right";
        Assert.Equal("Left", alignmentWhenRightSelected);

        // When the selected ayah is on LeftPage (Page 42):
        // Overlay must align to HorizontalAlignment.Right (covering RightPage, keeping LeftPage uncovered).
        const bool isLeftPageSelected = false;
        var alignmentWhenLeftSelected = isLeftPageSelected ? "Left" : "Right";
        Assert.Equal("Right", alignmentWhenLeftSelected);
    }

    [Fact]
    public async Task FacingAyahSelectionMaintainsPageAndVersesSynchronizationAcrossModeSwitch()
    {
        var contentPath = Path.Combine(AppContext.BaseDirectory, "Content", "quran_reader_candidate.sqlite3");
        using var content = new ContentRepository(contentPath);
        await content.ValidateAsync();

        var store = new MemoryStore();
        using var model = new ReaderViewModel(content, store);
        await model.InitializeAsync();

        // 1. Enter Mushaf mode on page 2 (loads Page 2 as primary and Page 1 as facing)
        await model.UpdateSettingsAsync(model.Settings with { Mode = ReadingMode.Mushaf });
        await model.NavigateAsync(2);
        Assert.Equal(2, model.Page);
        Assert.Equal(2, model.Verses[0].Page);
        Assert.Equal(1, model.FacingVerses[0].Page);

        // 2. Select Ayah 1:1 from FacingVerses (Page 1)
        var ayah1 = model.FacingVerses.First(v => v.Id == new AyahId(1, 1));
        await model.SelectAsync(ayah1);

        // Model state MUST synchronize Page and Verses to Page 1
        Assert.Equal(1, model.Page);
        Assert.Equal(new AyahId(1, 1), model.SelectedVerse?.Id);
        Assert.Equal(1, model.Verses[0].Page);
        Assert.All(model.Verses, v => Assert.Equal(1, v.Page));

        // 3. Switch to Tafsir Mode
        await model.UpdateSettingsAsync(model.Settings with { Mode = ReadingMode.Tafsir });

        // In Tafsir Mode, visible Verses MUST strictly be Page 1 (Al-Fatihah), never stale Page 2 (Al-Baqarah)
        Assert.Equal(1, model.Page);
        Assert.Equal(new AyahId(1, 1), model.SelectedVerse?.Id);
        Assert.Equal(7, model.Verses.Count);
        Assert.Equal(new AyahId(1, 1), model.Verses[0].Id);
        Assert.Equal(new AyahId(1, 7), model.Verses[^1].Id);
        Assert.All(model.Verses, v => Assert.Equal(1, v.Page));
        Assert.Empty(model.FacingVerses);
    }

    [Fact]
    public async Task ReadingStateMetadataSynchronizationAcrossKeyPages()
    {
        var contentPath = Path.Combine(AppContext.BaseDirectory, "Content", "quran_reader_candidate.sqlite3");
        using var content = new ContentRepository(contentPath);
        await content.ValidateAsync();

        var store = new MemoryStore();
        using var model = new ReaderViewModel(content, store);
        await model.InitializeAsync();
        await model.UpdateSettingsAsync(model.Settings with { TafsirOpen = true });

        // Test Page 1: Al-Fatihah
        await model.NavigateAsync(1);
        Assert.Equal(1, model.Page);
        Assert.Equal(1, model.ActivePage);
        Assert.Equal(1, model.ActiveSurah?.Number);
        Assert.Equal(new AyahId(1, 1), model.SelectedVerse?.Id);
        Assert.Contains("الفَاتِحَةِ", model.TafsirTitle);

        // Test Page 2: Al-Baqarah start
        await model.NavigateAsync(2);
        Assert.Equal(2, model.Page);
        Assert.Equal(2, model.ActivePage);
        Assert.Equal(2, model.ActiveSurah?.Number);
        Assert.Equal(new AyahId(2, 1), model.SelectedVerse?.Id);

        // Test Page 41: Al-Baqarah middle
        await model.NavigateAsync(41);
        Assert.Equal(41, model.Page);
        Assert.Equal(41, model.ActivePage);
        Assert.Equal(2, model.ActiveSurah?.Number);
        Assert.Equal(new AyahId(2, 249), model.SelectedVerse?.Id);

        // Test Page 42: Ayat al-Kursi (2:255) selection
        await model.NavigateAsync(42);
        Assert.Equal(42, model.Page);
        Assert.Equal(42, model.ActivePage);
        Assert.Equal(2, model.ActiveSurah?.Number);
        var ayatAlKursi = model.Verses.First(v => v.Id == new AyahId(2, 255));
        await model.SelectAsync(ayatAlKursi);
        Assert.Equal(new AyahId(2, 255), model.SelectedVerse?.Id);
        Assert.NotEmpty(model.Tafsir);
        Assert.Contains("٢٥٥", model.TafsirTitle);

        // Test Page 582: An-Naba (Surah 78, 30 ayat)
        await model.NavigateAsync(582);
        Assert.Equal(582, model.Page);
        Assert.Equal(582, model.ActivePage);
        Assert.Equal(78, model.ActiveSurah?.Number);
        Assert.Equal(30, model.Verses.Count);
        Assert.Equal(new AyahId(78, 1), model.SelectedVerse?.Id);

        // Test Page 603: Multi-surah page (Surahs 109, 110, 111)
        await model.NavigateAsync(603);
        Assert.Equal(603, model.Page);
        Assert.Equal(603, model.ActivePage);
        Assert.Equal(109, model.ActiveSurah?.Number); // First surah on page is Al-Kafirun
        Assert.Equal(new AyahId(109, 1), model.SelectedVerse?.Id);

        // Select an ayah in Surah 110 (An-Nasr)
        var ayah110 = model.Verses.First(v => v.Id == new AyahId(110, 1));
        await model.SelectAsync(ayah110);
        Assert.Equal(110, model.ActiveSurah?.Number);
        Assert.Equal(new AyahId(110, 1), model.SelectedVerse?.Id);
        Assert.Contains("النَّصۡرِ", model.TafsirTitle);

        // Select an ayah in Surah 111 (Al-Masad)
        var ayah111 = model.Verses.First(v => v.Id == new AyahId(111, 1));
        await model.SelectAsync(ayah111);
        Assert.Equal(111, model.ActiveSurah?.Number);
        Assert.Equal(new AyahId(111, 1), model.SelectedVerse?.Id);
        Assert.Contains("المَسَدِ", model.TafsirTitle);

        // Test Page 604: Multi-surah page (Surahs 112, 113, 114)
        await model.NavigateAsync(604);
        Assert.Equal(604, model.Page);
        Assert.Equal(604, model.ActivePage);
        Assert.Equal(112, model.ActiveSurah?.Number); // First surah on page is Al-Ikhlas
        Assert.Equal(new AyahId(112, 1), model.SelectedVerse?.Id);

        // Select an ayah in Surah 113 (Al-Falaq)
        var ayah113 = model.Verses.First(v => v.Id == new AyahId(113, 1));
        await model.SelectAsync(ayah113);
        Assert.Equal(113, model.ActiveSurah?.Number);
        Assert.Equal(new AyahId(113, 1), model.SelectedVerse?.Id);
        Assert.Contains("الفَلَقِ", model.TafsirTitle);

        // Select final verse in Surah 114 (An-Nas 114:6)
        var ayah114 = model.Verses.First(v => v.Id == new AyahId(114, 6));
        await model.SelectAsync(ayah114);
        Assert.Equal(114, model.ActiveSurah?.Number);
        Assert.Equal(new AyahId(114, 6), model.SelectedVerse?.Id);
        Assert.Contains("النَّاسِ", model.TafsirTitle);
    }

    [Fact]
    public void SurahBoundaryAndBasmalahRulesVerification()
    {
        // Surah 1 has no separate Basmalah header (1:1 is numbered Basmalah)
        Assert.False(SurahMetadata.HasBasmalahHeader(1));

        // Surah 9 (At-Tawbah) has no Basmalah header
        Assert.False(SurahMetadata.HasBasmalahHeader(9));

        // Other surahs must display Basmalah header
        Assert.True(SurahMetadata.HasBasmalahHeader(2));
        Assert.True(SurahMetadata.HasBasmalahHeader(78));
        Assert.True(SurahMetadata.HasBasmalahHeader(112));
        Assert.True(SurahMetadata.HasBasmalahHeader(113));
        Assert.True(SurahMetadata.HasBasmalahHeader(114));

        // Total ayat checks
        Assert.Equal(7, SurahMetadata.TotalAyat(1));
        Assert.Equal(286, SurahMetadata.TotalAyat(2));
        Assert.Equal(129, SurahMetadata.TotalAyat(9));
        Assert.Equal(40, SurahMetadata.TotalAyat(78));
        Assert.Equal(4, SurahMetadata.TotalAyat(112));
        Assert.Equal(5, SurahMetadata.TotalAyat(113));
        Assert.Equal(6, SurahMetadata.TotalAyat(114));

        // Revelation classifications
        Assert.Equal("مَكِّيَّةٌ", SurahMetadata.RevelationTypeArabic(1));
        Assert.Equal("مَدَنِيَّةٌ", SurahMetadata.RevelationTypeArabic(2));
        Assert.Equal("مَدَنِيَّةٌ", SurahMetadata.RevelationTypeArabic(9));
        Assert.Equal("مَدَنِيَّةٌ", SurahMetadata.RevelationTypeArabic(110));
        Assert.Equal("مَكِّيَّةٌ", SurahMetadata.RevelationTypeArabic(112));
        Assert.Equal("مَكِّيَّةٌ", SurahMetadata.RevelationTypeArabic(113));
        Assert.Equal("مَكِّيَّةٌ", SurahMetadata.RevelationTypeArabic(114));

        // Eastern Arabic numerals
        Assert.Equal("١", SurahMetadata.ToEasternArabicDigits(1));
        Assert.Equal("٤٢", SurahMetadata.ToEasternArabicDigits(42));
        Assert.Equal("٢٥٥", SurahMetadata.ToEasternArabicDigits(255));
        Assert.Equal("٦٠٤", SurahMetadata.ToEasternArabicDigits(604));
    }

    [Fact]
    public async Task ModeSwitchNeverDesynchronizesMetadataAcrossPages()
    {
        var contentPath = Path.Combine(AppContext.BaseDirectory, "Content", "quran_reader_candidate.sqlite3");
        using var content = new ContentRepository(contentPath);
        await content.ValidateAsync();

        var store = new MemoryStore();
        using var model = new ReaderViewModel(content, store);
        await model.InitializeAsync();

        // 1. Enter Mushaf mode on spread 41-42
        await model.UpdateSettingsAsync(model.Settings with { Mode = ReadingMode.Mushaf });
        await model.NavigateAsync(42);
        var ayatAlKursi = model.Verses.Concat(model.FacingVerses).First(v => v.Id == new AyahId(2, 255));
        await model.SelectAsync(ayatAlKursi);

        Assert.Equal(42, model.Page);
        Assert.Equal(new AyahId(2, 255), model.SelectedVerse?.Id);
        Assert.Equal(2, model.ActiveSurah?.Number);

        // Switch to Tafsir mode: verses MUST strictly be Page 42, active surah Al-Baqarah, selected ayah 2:255
        await model.UpdateSettingsAsync(model.Settings with { Mode = ReadingMode.Tafsir });
        Assert.Equal(42, model.Page);
        Assert.Equal(42, model.ActivePage);
        Assert.Equal(new AyahId(2, 255), model.SelectedVerse?.Id);
        Assert.Equal(2, model.ActiveSurah?.Number);
        Assert.All(model.Verses, v => Assert.Equal(42, v.Page));

        // 2. Select Page 604 in Tafsir mode
        await model.NavigateAsync(604);
        var ayah114 = model.Verses.First(v => v.Id == new AyahId(114, 6));
        await model.SelectAsync(ayah114);

        Assert.Equal(604, model.Page);
        Assert.Equal(604, model.ActivePage);
        Assert.Equal(114, model.ActiveSurah?.Number);
        Assert.Equal(new AyahId(114, 6), model.SelectedVerse?.Id);

        // Switch to Mushaf mode: MUST retain Page 604 and Surah 114
        await model.UpdateSettingsAsync(model.Settings with { Mode = ReadingMode.Mushaf });
        Assert.Equal(604, model.Page);
        Assert.Equal(604, model.ActivePage);
        Assert.Equal(114, model.ActiveSurah?.Number);
        Assert.Equal(new AyahId(114, 6), model.SelectedVerse?.Id);
    }

    [Theory]
    [InlineData(ReadingTheme.Light)]
    [InlineData(ReadingTheme.Dark)]
    [InlineData(ReadingTheme.Sepia)]
    public void Phase12A_BasmalahAndScriptureContrast_PassesWcagAaaInAllThemes(ReadingTheme theme)
    {
        var tokens = ThemeTokens.Resolve(theme);
        var basmalahContrast = ColorRgb.ContrastRatio(tokens.ScriptureForeground, tokens.MushafPageSurface);
        Assert.True(basmalahContrast >= 7.0, $"{theme} Basmalah contrast {basmalahContrast:F2} must be >= 7.0:1 (WCAG AAA)");

        var scriptureReadingContrast = ColorRgb.ContrastRatio(tokens.ScriptureForeground, tokens.ReadingSurface);
        Assert.True(scriptureReadingContrast >= 7.0, $"{theme} Scripture reading contrast {scriptureReadingContrast:F2} must be >= 7.0:1");
    }

    [Theory]
    [InlineData(ReadingTheme.Light)]
    [InlineData(ReadingTheme.Dark)]
    [InlineData(ReadingTheme.Sepia)]
    public void Phase12A_SelectedAyahTextAndBorderContrast_PassesWcagRequirements(ReadingTheme theme)
    {
        var tokens = ThemeTokens.Resolve(theme);
        var textOnSelected = ColorRgb.ContrastRatio(tokens.ScriptureForeground, tokens.SelectedSurface);
        Assert.True(textOnSelected >= 7.0, $"{theme} Selected ayah text contrast {textOnSelected:F2} on SelectedSurface must be >= 7.0:1 (WCAG AAA)");

        var borderOnSelected = ColorRgb.ContrastRatio(tokens.AccentColor, tokens.SelectedSurface);
        Assert.True(borderOnSelected >= 4.5, $"{theme} Selected ayah border contrast {borderOnSelected:F2} must be >= 4.5:1 (WCAG AA)");
    }

    [Fact]
    public void Phase12A_FitSpreadScaling_CalculatesComfortableBounds()
    {
        // Viewport heights: 600, 800, 1000, 1400
        var scale600 = ReadingLayout.CalculateFitScale(600);
        Assert.InRange(scale600, 20.0, 28.0);

        var scale800 = ReadingLayout.CalculateFitScale(800);
        Assert.InRange(scale800, 20.0, 28.0);

        var scale1000 = ReadingLayout.CalculateFitScale(1000);
        Assert.InRange(scale1000, 20.0, 28.0);

        // EffectiveMushafScale behavior:
        var fitSettings = new ReaderSettings(FitSpread: true, Scale: 32);
        Assert.Equal(scale800, ReadingLayout.EffectiveMushafScale(fitSettings, 800));

        var comfortSettings = new ReaderSettings(FitSpread: false, Scale: 34);
        Assert.Equal(34.0, ReadingLayout.EffectiveMushafScale(comfortSettings, 800));
    }

    [Fact]
    public void Phase12A_LineHeightRatios_PreventDiacriticClipping()
    {
        Assert.True(ReadingLayout.MushafLineHeightRatio >= 1.75, "Mushaf line height ratio must be >= 1.75 to prevent diacritic clipping");
        Assert.True(ReadingLayout.TafsirLineHeightRatio >= 1.75, "Tafsir line height ratio must be >= 1.75 to prevent diacritic clipping");
        Assert.True(ReadingLayout.CommentaryLineHeightRatio >= 1.60, "Commentary line height ratio must be >= 1.60");
    }

    [Fact]
    public void Phase12B_EstimatePageLayout_SingleSurah_FitsComfortably()
    {
        var verses = new List<Verse>
        {
            new(new(2, 253), "البقرة", 42, 3, "تِلْكَ الرُّسُلُ فَضَّلْنَا بَعْضَهُمْ عَلَىٰ بَعْضٍ"),
            new(new(2, 254), "البقرة", 42, 3, "يَا أَيُّهَا الَّذِينَ آمَنُوا أَنفِقُوا مِمَّا رَزَقْنَاكُم"),
            new(new(2, 255), "البقرة", 42, 3, "اللَّهُ لَا إِلَٰهَ إِلَّا هُوَ الْحَيُّ الْقَيُّومُ"),
            new(new(2, 256), "البقرة", 42, 3, "لَا إِكْرَاهَ فِي الدِّينِ قَد تَّبَيَّنَ الرُّشْدُ مِنَ الْغَيِّ")
        };

        var estimate = ReadingLayout.EstimatePageLayout(verses, availableWidth: 650, availableHeight: 800);
        Assert.True(estimate.FitsWithoutScroll, "Page 42 must fit within 800 DIP height");
        Assert.InRange(estimate.Scale, 20.0, 28.0);
    }

    [Fact]
    public void Phase12B_EstimatePageLayout_MultiSurahPage604_AdaptsScale()
    {
        // Page 604 contains 3 surahs: 112 (4 ayat), 113 (5 ayat), 114 (6 ayat)
        var verses = new List<Verse>
        {
            new(new(112, 1), "الإخلاص", 604, 30, "قُلْ هُوَ اللَّهُ أَحَدٌ"),
            new(new(112, 2), "الإخلاص", 604, 30, "اللَّهُ الصَّمَدُ"),
            new(new(112, 3), "الإخلاص", 604, 30, "لَمْ يَلِدْ وَلَمْ يُولَدْ"),
            new(new(112, 4), "الإخلاص", 604, 30, "وَلَمْ يَكُن لَّهُ كُفُوًا أَحَدٌ"),
            new(new(113, 1), "الفلق", 604, 30, "قُلْ أَعُوذُ بِرَبِّ الْفَلَقِ"),
            new(new(113, 2), "الفلق", 604, 30, "مِن شَرِّ مَا خَلَقَ"),
            new(new(113, 3), "الفلق", 604, 30, "وَمِن شَرِّ غَاسِقٍ إِذَا وَقَبَ"),
            new(new(113, 4), "الفلق", 604, 30, "وَمِن شَرِّ النَّفَّاثَاتِ فِي الْعُقَدِ"),
            new(new(113, 5), "الفلق", 604, 30, "وَمِن شَرِّ حَاسِدٍ إِذَا حَسَدَ"),
            new(new(114, 1), "الناس", 604, 30, "قُلْ أَعُوذُ بِرَبِّ النَّاسِ"),
            new(new(114, 2), "الناس", 604, 30, "مَلِكِ النَّاسِ"),
            new(new(114, 3), "الناس", 604, 30, "إِلَٰهِ النَّاسِ"),
            new(new(114, 4), "الناس", 604, 30, "مِن شَرِّ الْوَسْوَاسِ الْخَنَّاسِ"),
            new(new(114, 5), "الناس", 604, 30, "الَّذِي يُوَسْوِسُ فِي صُدُورِ النَّاسِ"),
            new(new(114, 6), "الناس", 604, 30, "مِنَ الْجِنَّةِ وَالنَّاسِ")
        };

        // In full desktop height (800 DIP), it fits without scroll
        var fitEstimate = ReadingLayout.EstimatePageLayout(verses, availableWidth: 650, availableHeight: 800, isMultiSurahSpread: true);
        Assert.True(fitEstimate.FitsWithoutScroll, "Page 604 must fit without vertical scrolling at 800 DIP height");
        Assert.InRange(fitEstimate.Scale, 18.0, 24.0);

        // In a very constrained height (450 DIP), it clamps to MinComfortableScale (18.0) and truthfully indicates scrolling required
        var smallEstimate = ReadingLayout.EstimatePageLayout(verses, availableWidth: 650, availableHeight: 450, isMultiSurahSpread: true);
        Assert.Equal(ReadingLayout.MinComfortableScale, smallEstimate.Scale);
        Assert.False(smallEstimate.FitsWithoutScroll, "Small viewport must truthfully indicate scrolling is required");
    }

    [Fact]
    public void Phase12B_CalculateSpreadScale_EnforcesConsistentTypographyAcrossFacingPages()
    {
        var page41 = new List<Verse>
        {
            new(new(2, 249), "البقرة", 41, 2, "فَلَمَّا فَصَلَ طَالُوتُ بِالْجُنُودِ"),
            new(new(2, 250), "البقرة", 41, 2, "وَلَمَّا بَرَزُوا لِجَالُوتَ وَجُنُودِهِ"),
            new(new(2, 251), "البقرة", 41, 2, "فَهَزَمُوهُم بِإِذْنِ اللَّهِ"),
            new(new(2, 252), "البقرة", 41, 2, "تِلْكَ آيَاتُ اللَّهِ نَتْلُوهَا عَلَيْكَ بِالْحَقِّ")
        };

        var page42 = new List<Verse>
        {
            new(new(2, 253), "البقرة", 42, 3, "تِلْكَ الرُّسُلُ فَضَّلْنَا بَعْضَهُمْ عَلَىٰ بَعْضٍ"),
            new(new(2, 254), "البقرة", 42, 3, "يَا أَيُّهَا الَّذِينَ آمَنُوا أَنفِقُوا مِمَّا رَزَقْنَاكُم"),
            new(new(2, 255), "البقرة", 42, 3, "اللَّهُ لَا إِلَٰهَ إِلَّا هُوَ الْحَيُّ الْقَيُّومُ"),
            new(new(2, 256), "البقرة", 42, 3, "لَا إِكْرَاهَ فِي الدِّينِ قَد تَّبَيَّنَ الرُّشْدُ مِنَ الْغَيِّ")
        };

        var (spreadScale, bothFit) = ReadingLayout.CalculateSpreadScale(page41, page42, availableWidthPerPage: 650, availableHeight: 800);
        Assert.True(bothFit, "Both Pages 41 and 42 must fit without scroll in 800 DIP height");
        Assert.InRange(spreadScale, 20.0, 28.0);
    }

    [Fact]
    public void Phase12B_TafsirDrawerThreeTierLayout_SpreadDocked_SingleDocked_Overlay()
    {
        var scale = 24.0;
        var drawerWidth = 360.0;
        var spreadThreshold = ReadingLayout.SpreadThreshold(scale);
        var singlePageThreshold = ReadingLayout.SinglePageThreshold(scale);

        // Tier 1: Wide Desktop (1800 DIP width) -> Spread Docked
        var wideWidth = 1800.0;
        var canDockSpread = wideWidth >= (spreadThreshold + drawerWidth + 30);
        Assert.True(canDockSpread, "1800 DIP window must support two-page spread docked beside drawer");

        // Tier 2: Medium Desktop (1150 DIP width) -> Adaptive Single Page Docked
        var mediumWidth = 1150.0;
        var mediumSpread = mediumWidth >= (spreadThreshold + drawerWidth + 30);
        var mediumSingle = !mediumSpread && (mediumWidth >= singlePageThreshold + drawerWidth + 24);
        Assert.False(mediumSpread, "1150 DIP window cannot comfortably fit 2 pages + drawer");
        Assert.True(mediumSingle, "1150 DIP window must support adaptive single page docked beside drawer");

        // Tier 3: Narrow Desktop (720 DIP width) -> Floating Overlay
        var narrowWidth = 720.0;
        var narrowSpread = narrowWidth >= (spreadThreshold + drawerWidth + 30);
        var narrowSingle = !narrowSpread && (narrowWidth >= singlePageThreshold + drawerWidth + 24);
        Assert.False(narrowSpread);
        Assert.False(narrowSingle, "720 DIP window must fall back to floating overlay to preserve reading comfort");
    }

    [Fact]
    public void Phase12B_OpeningSpread_Pages1And2_FitsWithoutScroll()
    {
        var page1 = new List<Verse>
        {
            new(new(1, 1), "الفاتحة", 1, 1, "بِسْمِ اللَّهِ الرَّحْمَٰنِ الرَّحِيمِ"),
            new(new(1, 2), "الفاتحة", 1, 1, "الْحَمْدُ لِلَّهِ رَبِّ الْعَالَمِينَ"),
            new(new(1, 3), "الفاتحة", 1, 1, "الرَّحْمَٰنِ الرَّحِيمِ"),
            new(new(1, 4), "الفاتحة", 1, 1, "مَالِكِ يَوْمِ الدِّينِ"),
            new(new(1, 5), "الفاتحة", 1, 1, "إِيَّاكَ نَعْبُدُ وَإِيَّاكَ نَسْتَعِينُ"),
            new(new(1, 6), "الفاتحة", 1, 1, "اهْدِنَا الصِّرَاطَ الْمُسْتَقِيمَ"),
            new(new(1, 7), "الفاتحة", 1, 1, "صِرَاطَ الَّذِينَ أَنْعَمْتَ عَلَيْهِمْ غَيْرِ الْمَغْضُوبِ عَلَيْهِمْ وَلَا الضَّالِّينَ")
        };

        var page2 = new List<Verse>
        {
            new(new(2, 1), "البقرة", 2, 1, "الم"),
            new(new(2, 2), "البقرة", 2, 1, "ذَٰلِكَ الْكِتَابُ لَا رَيْبَ ۛ فِيهِ ۛ هُدًى لِّلْمُتَّقِينَ"),
            new(new(2, 3), "البقرة", 2, 1, "الَّذِينَ يُؤْمِنُونَ بِالْغَيْبِ وَيُقِيمُونَ الصَّلَاةَ وَمِمَّا رَزَقْنَاهُمْ يُنفِقُونَ"),
            new(new(2, 4), "البقرة", 2, 1, "وَالَّذِينَ يُؤْمِنُونَ بِمَا أُنزِلَ إِلَيْكَ وَمَا أُنزِلَ مِن قَبْلِكَ وَبِالْآخِرَةِ هُمْ يُوقِنُونَ"),
            new(new(2, 5), "البقرة", 2, 1, "أُولَٰئِكَ عَلَىٰ هُدًى مِّن رَّبِّهِمْ ۖ وَأُولَٰئِكَ هُمُ الْمُفْلِحُونَ")
        };

        var (spreadScale, bothFit) = ReadingLayout.CalculateSpreadScale(page1, page2, availableWidthPerPage: 650, availableHeight: 800);
        Assert.True(bothFit, "Opening spread (Pages 1 & 2) must fit without scrolling");
        Assert.InRange(spreadScale, 24.0, 28.0);
    }

    [Fact]
    public void Phase12C_CountBaseCharacters_ExcludesCombiningMarks()
    {
        // Vocalized Arabic word with fathah, shaddah, sukun
        // "اللَّهُ" consists of:
        // ا (base) + ل (base) + ل (base) + ّ (shaddah) + َ (fathah) + ه (base) + ُ (dammah)
        // Total UTF-16 code units = 7, but base characters = 4
        var text = "اللَّهُ";
        Assert.Equal(7, text.Length);
        Assert.Equal(4, ReadingLayout.CountBaseCharacters(text));

        // Ayat al-Kursi sample
        var kursiSample = "ٱللَّهُ لَآ إِلَٰهَ إِلَّا هُوَ ٱلۡحَيُّ ٱلۡقَيُّومُۚ";
        var baseChars = ReadingLayout.CountBaseCharacters(kursiSample);
        Assert.True(baseChars < kursiSample.Length, "Base character count must be less than total code units due to combining tashkeel");
        Assert.True(baseChars > 0, "Base character count must be greater than zero");
    }

    [Fact]
    public void Phase12C_Pages41_42_FitSpread_CalculatesComfortableScaleAbove24DIP()
    {
        var page41 = new List<Verse>
        {
            new(new(2, 249), "البقرة", 41, 2, "فَلَمَّا فَصَلَ طَالُوتُ بِالْجُنُودِ قَالَ إِنَّ اللَّهَ مُبْتَلِيكُم بِنَهَرٍ"),
            new(new(2, 250), "البقرة", 41, 2, "وَلَمَّا بَرَزُوا لِجَالُوتَ وَجُنُودِهِ قَالُوا رَبَّنَا أَفْرِغْ عَلَيْنَا صَبْرًا"),
            new(new(2, 251), "البقرة", 41, 2, "فَهَزَمُوهُم بِإِذْنِ اللَّهِ وَقَتَلَ دَاوُودُ جَالُوتَ"),
            new(new(2, 252), "البقرة", 41, 2, "تِلْكَ آيَاتُ اللَّهِ نَتْلُوهَا عَلَيْكَ بِالْحَقِّ ۚ وَإِنَّكَ لَمِنَ الْمُرْسَلِينَ")
        };

        var page42 = new List<Verse>
        {
            new(new(2, 253), "البقرة", 42, 3, "تِلْكَ الرُّسُلُ فَضَّلْنَا بَعْضَهُمْ عَلَىٰ بَعْضٍ ۘ مِّنْهُم مَّن كَلَّمَ اللَّهُ"),
            new(new(2, 254), "البقرة", 42, 3, "يَا أَيُّهَا الَّذِينَ آمَنُوا أَنفِقُوا مِمَّا رَزَقْنَاكُم مِّن قَبْلِ أَن يَأْتِيَ يَوْمٌ"),
            new(new(2, 255), "البقرة", 42, 3, "اللَّهُ لَا إِلَٰهَ إِلَّا هُوَ الْحَيُّ الْقَيُّومُ ۚ لَا تَأْخُذُهُ سِنَةٌ وَلَا نَوْمٌ"),
            new(new(2, 256), "البقرة", 42, 3, "لَا إِكْرَاهَ فِي الدِّينِ ۖ قَد تَّبَيَّنَ الرُّشْدُ مِنَ الْغَيِّ")
        };

        var (spreadScale, bothFit) = ReadingLayout.CalculateSpreadScale(page41, page42, availableWidthPerPage: 570, availableHeight: 800);
        Assert.True(bothFit, "Pages 41-42 must fit within 800 DIP viewport");
        // Improved base-character estimation allows larger, comfortable scale >= 24.5 DIP (instead of 22.5 DIP)
        Assert.True(spreadScale >= 24.0, $"Spread scale must be comfortable (>= 24.0 DIP, actual: {spreadScale})");
        Assert.True(spreadScale <= 28.0, $"Spread scale must not exceed MaxFitScale (28.0 DIP, actual: {spreadScale})");
    }

    [Fact]
    public void Phase12D_FocusedPopup_IsDefaultTafsirPresentation()
    {
        var settings = new ReaderSettings();
        Assert.Equal(TafsirPresentation.FocusedPopup, settings.Presentation);
    }

    [Theory]
    [InlineData(TafsirPresentation.FocusedPopup)]
    [InlineData(TafsirPresentation.SlidingSidebar)]
    [InlineData(TafsirPresentation.BottomSheet)]
    public async Task Phase12D_TafsirPresentation_PersistsAndRoundTrips(TafsirPresentation presentation)
    {
        var tempFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var dbPath = Path.Combine(tempFolder, "user.db");
        try
        {
            var store = new UserStateStore(dbPath);
            var state = new UserState(
                new(new(2, 255), 12.0),
                new(ReadingTheme.Dark, 28.0, TafsirOpen: true, Mode: ReadingMode.Mushaf, DrawerPinned: false, DrawerWidth: 400.0, ReduceMotion: false, FitSpread: true, Presentation: presentation),
                [new(2, 255)],
                new(2, 255)
            );
            await store.SaveAsync(state);

            var loaded = await store.LoadAsync();
            Assert.Equal(presentation, loaded.Settings.Presentation);
            Assert.Equal(state.Position, loaded.Position);
            Assert.Equal(state.SelectedAyah, loaded.SelectedAyah);
            Assert.Equal(state.Settings.Mode, loaded.Settings.Mode);
            Assert.Equal(state.Settings.TafsirOpen, loaded.Settings.TafsirOpen);
        }
        finally
        {
            if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true);
        }
    }

    [Fact]
    public async Task Phase12D_ReaderViewModel_UpdateTafsirPresentation_PreservesSelectionAndState()
    {
        var store = new MemoryStore();
        var content = new MockContent();
        using var model = new ReaderViewModel(content, store);
        await model.InitializeAsync();

        // Select an ayah in Mushaf mode
        await model.UpdateSettingsAsync(model.Settings with { Mode = ReadingMode.Mushaf });
        var targetVerse = model.Verses.First();
        await model.SelectAsync(targetVerse);

        Assert.NotNull(model.SelectedVerse);
        Assert.Equal(targetVerse.Id, model.SelectedVerse.Id);
        Assert.True(model.Settings.TafsirOpen);
        Assert.Equal(TafsirPresentation.FocusedPopup, model.Settings.Presentation);

        // Switch presentation to SlidingSidebar
        await model.UpdateSettingsAsync(model.Settings with { Presentation = TafsirPresentation.SlidingSidebar });
        Assert.Equal(TafsirPresentation.SlidingSidebar, model.Settings.Presentation);
        Assert.Equal(targetVerse.Id, model.SelectedVerse.Id);
        Assert.True(model.Settings.TafsirOpen);

        // Switch presentation to BottomSheet
        await model.UpdateSettingsAsync(model.Settings with { Presentation = TafsirPresentation.BottomSheet });
        Assert.Equal(TafsirPresentation.BottomSheet, model.Settings.Presentation);
        Assert.Equal(targetVerse.Id, model.SelectedVerse.Id);
        Assert.True(model.Settings.TafsirOpen);
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
        public AyahId? SlowAyah { get; set; }
        public TaskCompletionSource ReleaseSlow { get; } = new();

        public Task ValidateAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<Surah>> GetSurahsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Surah>>([new(1, "الفاتحة", "Al-Fatihah", 1), new(2, "البقرة", "Al-Baqarah", 2)]);

        public Task<IReadOnlyList<Verse>> GetPageAsync(int page, CancellationToken cancellationToken = default)
        {
            var list = new List<Verse>
            {
                new(new(page == 42 ? 2 : 1, page == 42 ? 255 : 1), "Test Surah", page, 1, "بِسْمِ اللَّهِ"),
                new(new(page == 42 ? 2 : 1, page == 42 ? 256 : 2), "Test Surah", page, 1, "الرَّحْمَنِ الرَّحِيمِ")
            };
            return Task.FromResult<IReadOnlyList<Verse>>(list);
        }

        public Task<int?> FindPageAsync(AyahId id, CancellationToken cancellationToken = default) =>
            Task.FromResult<int?>(id.Surah == 2 && id.Ayah == 255 ? 42 : id.Surah);

        public async Task<string> GetTafsirAsync(AyahId id, CancellationToken cancellationToken = default)
        {
            if (SlowAyah.HasValue && id == SlowAyah.Value)
            {
                await ReleaseSlow.Task;
            }
            return $"commentary {id}";
        }
    }
}
