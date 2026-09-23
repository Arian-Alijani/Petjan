using System;
using System.Collections.Generic;
using Animora.Domain.Primitives;
using Xunit;

namespace Animora.Domain.Tests;

/// <summary>
/// The ULID contract the whole product depends on: 26 Crockford base32 chars,
/// lexicographically sortable, unique, and strictly increasing even inside one millisecond.
/// </summary>
public sealed class IdGeneratorTests
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    [Fact]
    public void NewId_Is26CrockfordBase32Chars()
    {
        var id = IdGenerator.NewId();

        Assert.Equal(26, id.Length);
        Assert.All(id, c => Assert.True(Alphabet.Contains(c, StringComparison.Ordinal), $"'{c}' is not Crockford base32"));
    }

    [Fact]
    public void Ids_SortByTimestamp()
    {
        var epoch = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);

        var earlier = IdGenerator.NewId(epoch);
        var later = IdGenerator.NewId(epoch.AddMilliseconds(1));

        Assert.True(string.CompareOrdinal(earlier, later) < 0);
    }

    [Fact]
    public void SameMillisecond_IdsAreStrictlyMonotonic()
    {
        var instant = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_123);

        var first = IdGenerator.NewId(instant);
        var second = IdGenerator.NewId(instant);

        Assert.Equal(first[..10], second[..10]); // identical timestamp prefix
        Assert.True(string.CompareOrdinal(first, second) < 0);
    }

    [Fact]
    public void TenThousandIds_AreUniqueAndStrictlyAscending()
    {
        const int count = 10_000;
        var seen = new HashSet<string>(count, StringComparer.Ordinal);
        var previous = string.Empty;

        for (var i = 0; i < count; i++)
        {
            var id = IdGenerator.NewId();

            Assert.True(seen.Add(id), $"duplicate id at index {i}");
            Assert.True(string.CompareOrdinal(previous, id) < 0, $"non-ascending id at index {i}");
            previous = id;
        }

        Assert.Equal(count, seen.Count);
    }
}
