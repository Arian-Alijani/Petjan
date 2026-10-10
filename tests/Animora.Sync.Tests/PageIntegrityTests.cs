using System.Security.Cryptography;
using System.Text;
using Animora.Sync.Custom;
using Xunit;

namespace Animora.Sync.Tests;

/// <summary>Facts for the feed hash rules.</summary>
public sealed class PageIntegrityTests
{
    [Fact]
    public void PageHash_IsLowercaseSha256OfRawBytes()
    {
        byte[] bytes = [1, 2, 3, 4];
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), PageIntegrity.ComputePageHash(bytes));
    }

    [Fact]
    public void ContentHash_IsSha256OfConcatenatedPageHashes()
    {
        string[] hashes = [PageIntegrity.ComputePageHash([1]), PageIntegrity.ComputePageHash([2])];
        var expected = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(hashes[0] + hashes[1]))).ToLowerInvariant();

        Assert.Equal(expected, PageIntegrity.ComputeContentHash(hashes));
        Assert.NotEqual(expected, PageIntegrity.ComputeContentHash([hashes[1], hashes[0]]));
    }
}
