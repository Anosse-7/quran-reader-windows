using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using QuranReader.Core;

namespace QuranReader.Data;

public sealed class ContentRepository(string path) : IContinuousContentRepository, IDisposable
{
    public const string PinnedSha256 = "c3cbc77cf9581f05e1e76ba3c6e89d0ef95bc84a65289a7139a9ba1d8859e6b9";
    private FileStream? verifiedFile;
    private bool validated;

    // Retain a read-only handle denying writes/deletion for the verified file's lifetime.
    public Task ValidateAsync(CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        if (validated) return;
        try
        {
            verifiedFile = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var hash = Convert.ToHexString(SHA256.HashData(verifiedFile));
            if (!hash.Equals(PinnedSha256, StringComparison.OrdinalIgnoreCase))
                throw new ResourceException("The content database failed its pinned SHA-256 check. Restore the original v0.3 package; content was not loaded.");
            using var db = Open();
            Require(db, "PRAGMA user_version", 3);
            using (var check = db.CreateCommand())
            {
                check.CommandText = "PRAGMA integrity_check";
                if (!Equals(check.ExecuteScalar(), "ok")) throw new ResourceException("SQLite content integrity check failed.");
                check.CommandText = "PRAGMA foreign_key_check";
                using var reader = check.ExecuteReader();
                if (reader.Read()) throw new ResourceException("Content contains broken foreign keys.");
            }
            Require(db, "SELECT count(*) FROM reader_verses", 6236);
            Require(db, "SELECT count(*) FROM reader_tafsir", 6236);
            Require(db, "SELECT count(DISTINCT sura_no) FROM reader_verses", 114);
            Require(db, "SELECT count(DISTINCT page) FROM reader_verses", 604);
            Require(db, "SELECT count(DISTINCT jozz) FROM reader_verses", 30);
            Require(db, "SELECT count(*) FROM reader_verses v JOIN reader_tafsir t USING(sura_no,aya_no) WHERE length(v.aya_text_unicode)>0 AND length(t.tafsir_markup)>0", 6236);
            cancellationToken.ThrowIfCancellationRequested();
            validated = true;
        }
        catch (OperationCanceledException)
        {
            verifiedFile?.Dispose(); verifiedFile = null;
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException or ResourceException)
        {
            verifiedFile?.Dispose();
            verifiedFile = null;
            throw new ResourceException($"Cannot open verified Qur’an content: {ex.Message}", ex);
        }
    }, cancellationToken);

    private static void Require(SqliteConnection db, string sql, long expected)
    {
        using var command = db.CreateCommand();
        command.CommandText = sql;
        if (Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) != expected)
            throw new ResourceException("The content schema, coverage, or tafsir mapping does not match v0.3.");
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(path),
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
        try { connection.Open(); return connection; }
        catch { connection.Dispose(); throw; }
    }

    private Task<T> ReadAsync<T>(Func<SqliteConnection, T> read, CancellationToken token) => Task.Run(() =>
    {
        if (!validated) throw new ResourceException("Content must be verified before reading.");
        token.ThrowIfCancellationRequested();
        try { using var db = Open(); var result = read(db); token.ThrowIfCancellationRequested(); return result; }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        { throw new ResourceException("The verified content could not be read. Restore the v0.3 data package.", ex); }
    }, token);

    public Task<bool> ValidateCheckpointPositionsAsync(IReadOnlyList<ReadingPosition> positions, CancellationToken token = default) => ReadAsync(db =>
    {
        using var command = db.CreateCommand();
        command.CommandText = "SELECT aya_text_unicode FROM reader_verses WHERE sura_no=$surah AND aya_no=$ayah";
        var surah = command.Parameters.Add("$surah", SqliteType.Integer);
        var ayah = command.Parameters.Add("$ayah", SqliteType.Integer);
        foreach (var group in positions.GroupBy(p => p.Ayah))
        {
            token.ThrowIfCancellationRequested();
            surah.Value = group.Key.Surah; ayah.Value = group.Key.Ayah;
            if (command.ExecuteScalar() is not string text || group.Any(p => p.Offset != 0 || !ContinuousScripture.IsBoundary(text, p.TextOffset))) return false;
        }
        return true;
    }, token);

    public Task<IReadOnlyList<Surah>> GetSurahsAsync(CancellationToken cancellationToken = default) => ReadAsync<IReadOnlyList<Surah>>(db =>
    {
        using var command = db.CreateCommand();
        command.CommandText = "SELECT sura_no,sura_name_ar,sura_name_en,min(page) FROM reader_verses GROUP BY sura_no ORDER BY sura_no";
        using var reader = command.ExecuteReader();
        var result = new List<Surah>();
        while (reader.Read()) result.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3)));
        return result;
    }, cancellationToken);

    public Task<IReadOnlyList<Verse>> GetPageAsync(int page, CancellationToken cancellationToken = default) => ReadAsync<IReadOnlyList<Verse>>(db =>
    {
        if (page is < 1 or > 604) throw new ArgumentOutOfRangeException(nameof(page));
        using var command = db.CreateCommand();
        command.CommandText = "SELECT sura_no,aya_no,sura_name_ar,page,jozz,aya_text_unicode FROM reader_verses WHERE page=$page ORDER BY sura_no,aya_no";
        command.Parameters.AddWithValue("$page", page);
        using var reader = command.ExecuteReader();
        var result = new List<Verse>();
        while (reader.Read()) result.Add(new(new(reader.GetInt32(0), reader.GetInt32(1)), reader.GetString(2), reader.GetInt32(3), reader.GetInt32(4), reader.GetString(5)));
        return result;
    }, cancellationToken);

    public Task<int?> FindPageAsync(AyahId id, CancellationToken cancellationToken = default) => ReadAsync<int?>(db =>
    {
        using var command = db.CreateCommand();
        command.CommandText = "SELECT page FROM reader_verses WHERE sura_no=$surah AND aya_no=$ayah";
        Bind(command, id);
        var value = command.ExecuteScalar();
        return value is null ? null : Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
    }, cancellationToken);

    public Task<IReadOnlyList<Verse>> GetSequenceAsync(AyahId start, int limit, CancellationToken cancellationToken = default) =>
        ReadSequenceAsync(start, limit, false, cancellationToken);

    public Task<IReadOnlyList<Verse>> GetPrecedingAsync(AyahId before, int limit, CancellationToken cancellationToken = default) =>
        ReadSequenceAsync(before, limit, true, cancellationToken);

    private Task<IReadOnlyList<Verse>> ReadSequenceAsync(AyahId anchor, int limit, bool preceding, CancellationToken token) => ReadAsync<IReadOnlyList<Verse>>(db =>
    {
        if (limit is < 1 or > ContinuousScripture.MaxVerses) throw new ArgumentOutOfRangeException(nameof(limit));
        using var command = db.CreateCommand();
        command.CommandText = preceding
            ? "SELECT sura_no,aya_no,sura_name_ar,page,jozz,aya_text_unicode FROM reader_verses WHERE sura_no<$surah OR (sura_no=$surah AND aya_no<$ayah) ORDER BY sura_no DESC,aya_no DESC LIMIT $limit"
            : "SELECT sura_no,aya_no,sura_name_ar,page,jozz,aya_text_unicode FROM reader_verses WHERE sura_no>$surah OR (sura_no=$surah AND aya_no>=$ayah) ORDER BY sura_no,aya_no LIMIT $limit";
        Bind(command, anchor);
        command.Parameters.AddWithValue("$limit", limit);
        using var reader = command.ExecuteReader();
        var result = new List<Verse>();
        while (reader.Read()) result.Add(new(new(reader.GetInt32(0), reader.GetInt32(1)), reader.GetString(2), reader.GetInt32(3), reader.GetInt32(4), reader.GetString(5)));
        if (preceding) result.Reverse();
        return result;
    }, token);

    public Task<string> GetTafsirAsync(AyahId id, CancellationToken cancellationToken = default) => ReadAsync(db =>
    {
        using var command = db.CreateCommand();
        command.CommandText = "SELECT tafsir_markup FROM reader_tafsir WHERE sura_no=$surah AND aya_no=$ayah";
        Bind(command, id);
        return command.ExecuteScalar() as string ?? throw new ResourceException($"Tafsir is missing for {id}.");
    }, cancellationToken);

    private static void Bind(SqliteCommand command, AyahId id)
    {
        command.Parameters.AddWithValue("$surah", id.Surah);
        command.Parameters.AddWithValue("$ayah", id.Ayah);
    }

    public void Dispose() { verifiedFile?.Dispose(); verifiedFile = null; validated = false; }
}
