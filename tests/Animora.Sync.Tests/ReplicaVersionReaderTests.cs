using Animora.Persistence.Tests;
using Animora.Sync.Abstractions;
using Animora.Sync.Custom;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Sync.Tests;

/// <summary>G05: <c>ExpectedServerVersion</c> source — the replica's observed version.</summary>
public sealed class ReplicaVersionReaderTests
{
    [Fact]
    public async Task PopulatesExpectedServerVersion_FromBase()
    {
        using var fixture = await ReplicaTestSupport.NewFixtureAsync();
        var store = ReplicaTestSupport.NewStore(fixture);
        var applied = await ReplicaTestSupport.ApplyAsync(store, ReplicaTestSupport.Batch("B1", 1, 0, 10),
            ReplicaTestSupport.Page("B1", 0, 1,
                ReplicaTestSupport.Rec("Pet", "P1", "ver-1", "rex"),
                ReplicaTestSupport.Rec("Pet", "P2", "ver-2", "tom"),
                ReplicaTestSupport.Rec("Pet", "P3", "ver-3", "gone", 'D')));
        Assert.True(applied.IsSuccess, applied.Detail);
        var reader = new ReplicaVersionReader(fixture.Services.GetRequiredService<IServiceScopeFactory>());

        Assert.Equal("ver-1", await reader.ReadServerVersionAsync("Pet", "P1"));
        Assert.Equal("ver-2", await reader.ReadServerVersionAsync("Pet", "P2"));
        Assert.Null(await reader.ReadServerVersionAsync("Pet", "P3")); // tombstone ⇒ no precondition
        Assert.Null(await reader.ReadServerVersionAsync("Pet", "MISSING"));
        Assert.Null(await reader.ReadServerVersionAsync("Visit", "P1"));
    }

    [Fact]
    public async Task EmptyReplica_ReturnsNull()
    {
        using var fixture = await ReplicaTestSupport.NewFixtureAsync();
        var reader = new ReplicaVersionReader(fixture.Services.GetRequiredService<IServiceScopeFactory>());
        Assert.Null(await reader.ReadServerVersionAsync("Pet", "P1"));
        Assert.Null(await reader.ReadServerVersionAsync("", "P1"));
    }
}
