using System.Security.Cryptography;
using System.Text.Json;
using QuranReader.Core;

namespace QuranReader.Data;

/// <summary>Disposable, bounded derived metadata; never a source of scripture or user progress.</summary>
public sealed class PaginationCheckpointStore(string directory, string compatibility,
    Func<IReadOnlyList<ReadingPosition>, CancellationToken, Task<bool>> validatePositions)
{
    public const int FormatVersion = 1;
    public const int MaximumFiles = 8;
    public const int MaximumBytes = 4 * 1024 * 1024;
    private readonly SemaphoreSlim writes = new(1, 1);
    private byte[]? serializationBuffer;
    private sealed record Payload(AdaptiveLayoutKey Layout, string Compatibility, AdaptiveWindowBoundary[] Windows);
    private sealed record Envelope(int Version, string Digest, Payload Payload);
    private sealed class CheckpointSizeException : IOException;
    private sealed class BoundedBufferStream(byte[] buffer) : MemoryStream(buffer, 0, buffer.Length, true, true)
    {
        public override void Write(byte[] value, int offset, int count) { Check(count); base.Write(value, offset, count); }
        public override void Write(ReadOnlySpan<byte> value) { Check(value.Length); base.Write(value); }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> value, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); Write(value.Span); return ValueTask.CompletedTask; }
        public override Task WriteAsync(byte[] value, int offset, int count, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); Write(value, offset, count); return Task.CompletedTask; }
        private void Check(int count) { if (Position + count > Capacity) throw new CheckpointSizeException(); }
    }
    public string LastResult { get; private set; } = "unused";

    public string FilePath(AdaptiveLayoutKey layout) => Path.Combine(directory,
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { layout, compatibility, FormatVersion }))) + ".json");

    public async Task<AdaptivePaginationIndex?> LoadAsync(AdaptiveLayoutKey layout, CancellationToken token = default)
    {
        var path = FilePath(layout);
        try
        {
            var loaded = await Task.Run(async () =>
            {
                if (!File.Exists(path)) { LastResult = "miss"; return null; }
                using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
                if (input.Length > MaximumBytes) throw new InvalidDataException("Oversized checkpoint.");
                var envelope = await JsonSerializer.DeserializeAsync<Envelope>(input, cancellationToken: token);
                if (envelope?.Payload is not { } payload || envelope.Version != FormatVersion || payload.Layout != layout ||
                    payload.Compatibility != compatibility || payload.Windows is null || envelope.Digest != Digest(payload))
                    throw new InvalidDataException("Incompatible or incomplete checkpoint.");
                return AdaptivePaginationIndex.Restore(layout, payload.Windows);
            }, token);
            if (loaded is null) return null;
            var positions = loaded.Windows.SelectMany(w => w.Leaves.SelectMany(l => new[] { l.Start, l.End })).Distinct().ToArray();
            if (!await validatePositions(positions, token)) throw new InvalidDataException("Checkpoint splits or exceeds pinned source.");
            LastResult = "validated-hit";
            return loaded;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ResourceException)
        {
            // A derived cache failure is a miss, never a content repair or a
            // swallowed UI exception. Keep the reason visible to diagnostics.
            LastResult = "rejected: " + ex.Message;
            return null;
        }
    }

    public async Task<bool> SaveAsync(AdaptiveLayoutKey layout, AdaptiveWindowBoundary[] windows, CancellationToken token = default)
    {
        await writes.WaitAsync(token);
        try
        {
            return await Task.Run(async () =>
            {
                _ = AdaptivePaginationIndex.Restore(layout, windows);
                Directory.CreateDirectory(directory);
                var payload = new Payload(layout, compatibility, windows);
                // Reuse one bounded buffer instead of allocating several large
                // JSON byte arrays whenever the canonical prefix grows.
                serializationBuffer ??= new byte[MaximumBytes];
                using var serialized = new BoundedBufferStream(serializationBuffer);
                serialized.SetLength(0);
                try { await JsonSerializer.SerializeAsync(serialized, payload, cancellationToken: token); }
                catch (CheckpointSizeException) { LastResult = "write omitted: size limit"; return false; }
                var digest = Convert.ToHexString(SHA256.HashData(serializationBuffer.AsSpan(0, (int)serialized.Length)));
                var destination = FilePath(layout);
                var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
                    {
                        await JsonSerializer.SerializeAsync(output, new Envelope(FormatVersion, digest, payload), cancellationToken: token);
                        if (output.Length > MaximumBytes) { LastResult = "write omitted: size limit"; return false; }
                        await output.FlushAsync(token);
                        output.Flush(true);
                    }
                    token.ThrowIfCancellationRequested();
                    File.Move(temporary, destination, true);
                    // An abandoned interrupted write is never imported. Bound
                    // stale temporary metadata as well as completed generations.
                    foreach (var stale in new DirectoryInfo(directory).GetFiles("*.tmp").Where(f => f.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-1))) stale.Delete();
                    foreach (var obsolete in new DirectoryInfo(directory).GetFiles("*.json").OrderByDescending(f => f.LastWriteTimeUtc).Skip(MaximumFiles)) obsolete.Delete();
                    LastResult = "saved";
                    return true;
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }, token);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { LastResult = "write unavailable: " + ex.Message; return false; }
        finally { writes.Release(); }
    }

    private static string Digest(Payload payload) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(payload)));
}
