using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using QuranReader.Core;
using QuranReader.Data;

namespace QuranReader.Tests;

public sealed class DataTests
{
    private static string ContentPath => Path.Combine(AppContext.BaseDirectory, "Content", "quran_reader_candidate.sqlite3");

    [Fact]
    public async Task MissingOrModifiedPublisherFontIsRejected()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ttf");
        try
        {
            using var font = new PublisherFont();
            await Assert.ThrowsAsync<ResourceException>(() => font.VerifyAsync(path));
            await File.WriteAllTextAsync(path, "invalid font");
            await Assert.ThrowsAsync<ResourceException>(() => font.VerifyAsync(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task AllPagesAndTafsirAreExactAndContentIsUnchanged()
    {
        var before = await File.ReadAllBytesAsync(ContentPath);
        using var content = new ContentRepository(ContentPath);
        var clock = Stopwatch.StartNew();
        await content.ValidateAsync();
        Assert.Equal(114, (await content.GetSurahsAsync()).Count);
        var ids = new HashSet<AyahId>();
        using var direct = new SqliteConnection($"Data Source={ContentPath};Mode=ReadOnly;Pooling=False");
        direct.Open();
        using var original = direct.CreateCommand();
        original.CommandText = "SELECT q.text,t.text_markup FROM quran_texts q JOIN tafsir t USING(sura_no,aya_no) WHERE q.source_id='kfgqpc-hafs-unicode-v15' AND t.source_id='kfgqpc-tafsir-muyassar-v3' AND q.sura_no=$s AND q.aya_no=$a";
        var s = original.Parameters.Add("$s", SqliteType.Integer);
        var a = original.Parameters.Add("$a", SqliteType.Integer);
        for (var page = 1; page <= 604; page++)
        {
            var verses = await content.GetPageAsync(page);
            Assert.NotEmpty(verses);
            foreach (var verse in verses)
            {
                Assert.True(ids.Add(verse.Id));
                Assert.Equal(page, verse.Page);
                s.Value = verse.Id.Surah; a.Value = verse.Id.Ayah;
                using var row = original.ExecuteReader();
                Assert.True(row.Read());
                Assert.Equal(row.GetString(0), verse.Text);
                Assert.Equal(row.GetString(1), await content.GetTafsirAsync(verse.Id));
            }
        }
        Assert.Equal(6236, ids.Count);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(60), $"Full dataset traversal: {clock.Elapsed}");
        Assert.Equal(before, await File.ReadAllBytesAsync(ContentPath));
    }

    [Fact]
    public async Task VerifiedContentDeniesWritesAndReplacement()
    {
        using var content = new ContentRepository(ContentPath);
        await content.ValidateAsync();
        Assert.Throws<IOException>(() => { using var file = new FileStream(ContentPath, FileMode.Open, FileAccess.Write, FileShare.Read); });
        Assert.False(File.Exists(ContentPath + "-wal"));
        Assert.False(File.Exists(ContentPath + "-journal"));
    }

    [Fact]
    public async Task MissingOrTamperedDatabaseFailsClosed()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
        try
        {
            using var missing = new ContentRepository(path);
            await Assert.ThrowsAsync<ResourceException>(() => missing.ValidateAsync());
            await Assert.ThrowsAsync<ResourceException>(() => missing.GetPageAsync(1));
            await File.WriteAllTextAsync(path, "invalid database");
            using var damaged = new ContentRepository(path);
            await Assert.ThrowsAsync<ResourceException>(() => damaged.ValidateAsync());
            var bytes = await File.ReadAllBytesAsync(ContentPath);
            bytes[^1] ^= 1;
            await File.WriteAllBytesAsync(path, bytes);
            using var changed = new ContentRepository(path);
            await Assert.ThrowsAsync<ResourceException>(() => changed.ValidateAsync());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task LookupBoundsCancellationAndRepeatedNavigation()
    {
        using var content = new ContentRepository(ContentPath);
        await content.ValidateAsync();
        Assert.Equal(42, await content.FindPageAsync(new(2, 255)));
        Assert.Null(await content.FindPageAsync(new(999, 1)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => content.GetPageAsync(0));
        await Assert.ThrowsAsync<ResourceException>(() => content.GetTafsirAsync(new(999, 1)));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => content.GetPageAsync(1, cancelled.Token));
        var clock = Stopwatch.StartNew();
        for (var i = 0; i < 100; i++) await content.GetPageAsync(i + 1);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"100 page queries: {clock.Elapsed}");
    }

    [Fact]
    public async Task PreferencesRoundTripSeparatelyAndAtomically()
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var path = Path.Combine(folder, "user.db");
        try
        {
            var store = new UserStateStore(path);
            Assert.Equal(UserState.Default.Position, (await store.LoadAsync()).Position);
            var expected = new UserState(new(new(2, 255), -42.5), new(ReadingTheme.Dark, 44, true), [new(2, 255), new(114, 6)]);
            await store.SaveAsync(expected);
            var actual = await new UserStateStore(path).LoadAsync();
            Assert.Equal(expected.Position, actual.Position);
            Assert.Equal(expected.Settings, actual.Settings);
            Assert.Equal(expected.Bookmarks, actual.Bookmarks);
            await store.SaveAsync(expected with { Bookmarks = [] });
            Assert.Empty((await store.LoadAsync()).Bookmarks);
            Assert.Equal(ContentRepository.PinnedSha256, Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(ContentPath))).ToLowerInvariant());
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task CorruptUserDatabaseReportsRecoverableError()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
        try
        {
            await File.WriteAllTextAsync(path, "broken");
            var store = new UserStateStore(path);
            await Assert.ThrowsAsync<ResourceException>(() => store.LoadAsync());
            await Assert.ThrowsAsync<ResourceException>(() => store.SaveAsync(UserState.Default));
        }
        finally { File.Delete(path); }
    }
}
