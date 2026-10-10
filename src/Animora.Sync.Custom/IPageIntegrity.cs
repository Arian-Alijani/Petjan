using System.Security.Cryptography;
using System.Text;

namespace Animora.Sync.Custom;

/// <summary>
/// Hash rules of the immutable-batch feed (mirrors the server's FeedPageWriter): a page hash is SHA-256 (lowercase hex) over the
/// raw gzip bytes as downloaded (it is also the ETag); the batch content hash is SHA-256 over the concatenated ASCII page hashes.
/// </summary>
public static class PageIntegrity
{
    /// <summary>SHA-256 (lowercase hex) of the raw gzip bytes of one page.</summary>
    public static string ComputePageHash(ReadOnlySpan<byte> gzipBytes) =>
        Convert.ToHexStringLower(SHA256.HashData(gzipBytes));

    /// <summary>SHA-256 (lowercase hex) over the ASCII concatenation of the per-page hashes in page order.</summary>
    public static string ComputeContentHash(IReadOnlyList<string> pageHashes)
    {
        ArgumentNullException.ThrowIfNull(pageHashes);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(string.Concat(pageHashes))));
    }
}
