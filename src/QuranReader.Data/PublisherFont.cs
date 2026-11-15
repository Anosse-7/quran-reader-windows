using System.Security.Cryptography;
using QuranReader.Core;

namespace QuranReader.Data;

public sealed class PublisherFont : IDisposable
{
    public const string PinnedSha256 = "9ccd82324319bfe9e57efd34c830770ce86655f01661ca06b50e3b0c20073c57";
    private FileStream? stream;

    public Task VerifyAsync(string path) => Task.Run(() =>
    {
        try
        {
            stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(PinnedSha256, StringComparison.OrdinalIgnoreCase))
                throw new ResourceException("Publisher font integrity check failed. Run tools/Initialize-LocalContent.ps1 with the verified Hafs archive.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ResourceException)
        {
            stream?.Dispose(); stream = null;
            throw new ResourceException("The verified Hafs font is missing or modified. Run tools/Initialize-LocalContent.ps1 before reading.", ex);
        }
    });

    public void Dispose() => stream?.Dispose();
}
