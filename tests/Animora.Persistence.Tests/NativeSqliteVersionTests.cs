using System;
using System.IO;
using Animora.Domain.Results;
using Animora.Infrastructure.Local.Startup;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Animora.Persistence.Tests;

/// <summary>
/// T-RULE 004: the native SQLite actually loaded at runtime is asserted (a package version is never
/// proof). The loaded library must be ≥ 3.50.2 and an older or unparsable version is
/// <c>NATIVE_SQLITE_TOO_OLD</c>.
/// </summary>
public sealed class NativeSqliteVersionTests
{
    [Fact]
    public void LoadedNativeVersion_IsAtLeastTheMinimum()
    {
        var path = Path.Combine(Path.GetTempPath(), "animora-native-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            using var connection = new SqliteConnection($"Data Source={path}");
            connection.Open();

            var result = NativeSqliteAssertion.Assert(connection);

            Assert.True(result.IsSuccess, result.Detail);
            var parsed = NativeSqliteAssertion.Parse(connection.ServerVersion);
            Assert.True(parsed.IsSuccess);
            Assert.True(parsed.Value! >= NativeSqliteAssertion.Minimum);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Theory]
    [InlineData("3.50.1")] // one patch below the minimum.
    [InlineData("3.49.9")]
    [InlineData("3.50")]
    [InlineData("not-a-version")]
    [InlineData("")]
    public void Parse_OfAnOlderOrMalformedVersion_IsNATIVE_SQLITE_TOO_OLD(string version)
    {
        var parsed = NativeSqliteAssertion.Parse(version);

        if (parsed.IsSuccess)
        {
            // A parsable-but-old version parses, and is below the minimum the assertion enforces.
            Assert.True(parsed.Value! < NativeSqliteAssertion.Minimum);
        }
        else
        {
            Assert.Equal(ErrorCodes.NativeSqliteTooOld, parsed.Error);
        }
    }

    [Fact]
    public void Parse_OfTheMinimum_IsAccepted()
    {
        var parsed = NativeSqliteAssertion.Parse("3.50.2");

        Assert.True(parsed.IsSuccess);
        Assert.Equal(new Version(3, 50, 2), parsed.Value);
        Assert.False(parsed.Value! < NativeSqliteAssertion.Minimum);
    }
}
