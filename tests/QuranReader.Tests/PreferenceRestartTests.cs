using Microsoft.Data.Sqlite;
using QuranReader.Core;
using QuranReader.Data;

namespace QuranReader.Tests;

public sealed class PreferenceRestartTests
{
    [Theory]
    [InlineData(18, false)]
    [InlineData(20, false)]
    [InlineData(22, true)]
    [InlineData(24, false)]
    [InlineData(48, false)]
    [InlineData(56, false)]
    public async Task ReadingScaleAndFitChoiceSurviveFreshStoreAndReader(double scale, bool fit)
    {
        var folder = Path.Combine(Path.GetTempPath(), "QuranReader.Tests", Guid.NewGuid().ToString());
        try
        {
            var path = Path.Combine(folder, "preferences.db");
            var expected = new UserState(new(new(2, 255), -37),
                new(ReadingTheme.Dark, scale, Mode: ReadingMode.Mushaf, FitSpread: fit),
                [new(2, 255)], new(2, 253));
            await new UserStateStore(path).SaveAsync(expected);
            var actual = await new UserStateStore(path).LoadAsync();
            Assert.Equal(expected.Position, actual.Position);
            Assert.Equal(expected.Settings, actual.Settings);
            Assert.Equal(expected.SelectedAyah, actual.SelectedAyah);
            Assert.Equal(expected.Bookmarks, actual.Bookmarks);

            var contentPath = Path.Combine(AppContext.BaseDirectory, "Content", "quran_reader_candidate.sqlite3");
            using var content = new ContentRepository(contentPath);
            using var model = new ReaderViewModel(content, new UserStateStore(path));
            await model.InitializeAsync();
            Assert.True(model.Ready, model.Error);
            Assert.Equal(expected.Position, model.Position);
            Assert.Equal(expected.SelectedAyah, model.SelectedVerse?.Id);
            Assert.Equal(expected.Settings, model.Settings);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task LegacyPresentationMigratesWithoutLosingPositionSelectionOrBookmarks()
    {
        var folder = Path.Combine(Path.GetTempPath(), "QuranReader.Tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "preferences.db");
        try
        {
            using (var db = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                db.Open();
                using var cmd = db.CreateCommand();
                cmd.CommandText = """
                    CREATE TABLE state(id INTEGER PRIMARY KEY,surah INTEGER,ayah INTEGER,offset REAL,theme INTEGER,scale REAL,tafsir INTEGER);
                    INSERT INTO state VALUES(1,2,255,-37,2,56,0);
                    CREATE TABLE bookmarks(surah INTEGER,ayah INTEGER,PRIMARY KEY(surah,ayah));
                    INSERT INTO bookmarks VALUES(2,255);
                    CREATE TABLE presentation(id INTEGER PRIMARY KEY,mode INTEGER,pinned INTEGER,width REAL,reduced INTEGER,selected_surah INTEGER,selected_ayah INTEGER);
                    INSERT INTO presentation VALUES(1,1,0,380,1,2,253);
                    """;
                cmd.ExecuteNonQuery();
            }
            var store = new UserStateStore(path);
            var state = await store.LoadAsync();
            Assert.Equal(new ReadingPosition(new(2, 255), -37), state.Position);
            Assert.Equal(new AyahId(2, 253), state.SelectedAyah);
            Assert.Equal([new AyahId(2, 255)], state.Bookmarks);
            Assert.True(state.Settings.FitSpread);
            Assert.Equal(TafsirPresentation.FocusedPopup, state.Settings.Presentation);
            Assert.Equal(56, state.Settings.Scale);
            var updated = state with { Settings = state.Settings with { FitSpread = false } };
            await store.SaveAsync(updated);
            Assert.Equal(updated.Settings, (await new UserStateStore(path).LoadAsync()).Settings);
        }
        finally { Directory.Delete(folder, true); }
    }
}
