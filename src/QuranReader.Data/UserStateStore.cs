using Microsoft.Data.Sqlite;
using QuranReader.Core;

namespace QuranReader.Data;

public sealed class UserStateStore(string path) : IUserStateStore
{
    private SqliteConnection Open()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        try
        {
            db.Open();
            using var command = db.CreateCommand();
            command.CommandText = """
            PRAGMA busy_timeout=5000;
            CREATE TABLE IF NOT EXISTS state(id INTEGER PRIMARY KEY CHECK(id=1),surah INTEGER NOT NULL,ayah INTEGER NOT NULL,offset REAL NOT NULL,theme INTEGER NOT NULL,scale REAL NOT NULL,tafsir INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS bookmarks(surah INTEGER NOT NULL,ayah INTEGER NOT NULL,PRIMARY KEY(surah,ayah));
            CREATE TABLE IF NOT EXISTS presentation(id INTEGER PRIMARY KEY CHECK(id=1),mode INTEGER NOT NULL,pinned INTEGER NOT NULL,width REAL NOT NULL,reduced INTEGER NOT NULL,selected_surah INTEGER,selected_ayah INTEGER,tafsir_presentation INTEGER DEFAULT 0);
            """;
            command.ExecuteNonQuery();
            using var migration = db.BeginTransaction();
            command.Transaction = migration;
            foreach (var column in new[] { (Name: "tafsir_presentation", Default: 0), (Name: "fit_spread", Default: 1), (Name: "text_offset", Default: 0) })
            {
                command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('presentation') WHERE name=$name";
                command.Parameters.Clear();
                command.Parameters.AddWithValue("$name", column.Name);
                if (Convert.ToInt64(command.ExecuteScalar()) == 0)
                {
                    // Identifiers/defaults are internal constants, never user input.
                    command.CommandText = $"ALTER TABLE presentation ADD COLUMN {column.Name} INTEGER NOT NULL DEFAULT {column.Default};";
                    command.Parameters.Clear();
                    command.ExecuteNonQuery();
                }
            }
            migration.Commit();
            return db;
        }
        catch { db.Dispose(); throw; }
    }

    public Task<UserState> LoadAsync(CancellationToken cancellationToken = default) => RunAsync(() =>
    {
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = "SELECT surah,ayah,offset,theme,scale,tafsir FROM state WHERE id=1";
        var state = UserState.Default;
        using (var reader = command.ExecuteReader())
        {
            if (reader.Read())
            {
                var theme = reader.GetInt32(3);
                var scale = reader.GetDouble(4);
                var offset = reader.GetDouble(2);
                if (!Enum.IsDefined((ReadingTheme)theme) || !double.IsFinite(scale) ||
                    scale < ReadingLayout.MinComfortableScale || scale > ReadingLayout.MaxManualScale || !double.IsFinite(offset))
                    throw new ResourceException("Saved settings are invalid. Back up and reset the separate user database.");
                state = new(new(new(reader.GetInt32(0), reader.GetInt32(1)), offset), new((ReadingTheme)theme, scale, reader.GetBoolean(5)), []);
            }
        }
        command.CommandText = "SELECT mode,pinned,width,reduced,selected_surah,selected_ayah,tafsir_presentation,fit_spread,text_offset FROM presentation WHERE id=1";
        using (var reader = command.ExecuteReader())
        {
            if (reader.Read())
            {
                var mode = reader.GetInt32(0);
                var width = reader.GetDouble(2);
                if (!Enum.IsDefined((ReadingMode)mode) || !double.IsFinite(width) || width is < 300 or > 560)
                    throw new ResourceException("Saved presentation preferences are invalid. Back up and reset the separate user database.");
                var presentation = reader.FieldCount > 6 && !reader.IsDBNull(6) ? reader.GetInt32(6) : 0;
                if (!Enum.IsDefined((TafsirPresentation)presentation)) presentation = 0;
                var fitSpread = reader.GetInt32(7);
                if (fitSpread is not (0 or 1)) throw new ResourceException("Saved Fit Spread preference is invalid. Back up and reset the separate user database.");
                var textOffset = reader.GetInt32(8);
                if (textOffset < 0) throw new ResourceException("Saved scripture offset is invalid. Back up and reset the separate user database.");
                state = state with
                {
                    Position = state.Position with { TextOffset = textOffset },
                    Settings = state.Settings with
                    {
                        Mode = (ReadingMode)mode,
                        DrawerPinned = reader.GetBoolean(1),
                        DrawerWidth = width,
                        ReduceMotion = reader.GetBoolean(3),
                        Presentation = (TafsirPresentation)presentation,
                        FitSpread = fitSpread == 1
                    },
                    SelectedAyah = reader.IsDBNull(4) || reader.IsDBNull(5) ? null : new(reader.GetInt32(4), reader.GetInt32(5))
                };
            }
        }
        command.CommandText = "SELECT surah,ayah FROM bookmarks ORDER BY surah,ayah";
        using var rows = command.ExecuteReader();
        var bookmarks = new List<AyahId>();
        while (rows.Read()) bookmarks.Add(new(rows.GetInt32(0), rows.GetInt32(1)));
        return state with { Bookmarks = bookmarks };
    }, cancellationToken);

    public Task SaveAsync(UserState state, CancellationToken cancellationToken = default) => RunAsync(() =>
    {
        using var db = Open();
        using var transaction = db.BeginTransaction();
        using var command = db.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT OR REPLACE INTO state VALUES(1,$s,$a,$o,$t,$z,$p)";
        command.Parameters.AddWithValue("$s", state.Position.Ayah.Surah);
        command.Parameters.AddWithValue("$a", state.Position.Ayah.Ayah);
        command.Parameters.AddWithValue("$o", state.Position.Offset);
        command.Parameters.AddWithValue("$t", (int)state.Settings.Theme);
        command.Parameters.AddWithValue("$z", state.Settings.Scale);
        command.Parameters.AddWithValue("$p", state.Settings.TafsirOpen);
        command.ExecuteNonQuery();
        command.Parameters.Clear();
        command.CommandText = "INSERT OR REPLACE INTO presentation (id,mode,pinned,width,reduced,selected_surah,selected_ayah,tafsir_presentation,fit_spread,text_offset) VALUES(1,$m,$p,$w,$r,$ss,$sa,$tp,$fit,$text)";
        command.Parameters.AddWithValue("$m", (int)state.Settings.Mode);
        command.Parameters.AddWithValue("$p", state.Settings.DrawerPinned);
        command.Parameters.AddWithValue("$w", state.Settings.DrawerWidth);
        command.Parameters.AddWithValue("$r", state.Settings.ReduceMotion);
        command.Parameters.AddWithValue("$ss", (object?)state.SelectedAyah?.Surah ?? DBNull.Value);
        command.Parameters.AddWithValue("$sa", (object?)state.SelectedAyah?.Ayah ?? DBNull.Value);
        command.Parameters.AddWithValue("$tp", (int)state.Settings.Presentation);
        command.Parameters.AddWithValue("$fit", state.Settings.FitSpread);
        command.Parameters.AddWithValue("$text", state.Position.TextOffset);
        command.ExecuteNonQuery();
        command.CommandText = "DELETE FROM bookmarks";
        command.Parameters.Clear();
        command.ExecuteNonQuery();
        command.CommandText = "INSERT INTO bookmarks VALUES($s,$a)";
        var surah = command.Parameters.Add("$s", SqliteType.Integer);
        var ayah = command.Parameters.Add("$a", SqliteType.Integer);
        foreach (var bookmark in state.Bookmarks.Distinct())
        {
            surah.Value = bookmark.Surah; ayah.Value = bookmark.Ayah; command.ExecuteNonQuery();
        }
        transaction.Commit();
        return true;
    }, cancellationToken);

    private static Task<T> RunAsync<T>(Func<T> work, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        try { return work(); }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        { throw new ResourceException("Cannot read or save local preferences. Check disk space and permissions; the content database is untouched.", ex); }
    }, token);
}
