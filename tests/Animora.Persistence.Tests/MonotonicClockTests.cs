using System;
using System.Threading.Tasks;
using Animora.Domain.Time;
using Animora.Infrastructure.Local.Time;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Persistence.Tests;

/// <summary>
/// A11d's rollback-resistant clock: <see cref="MonotonicClock.HighWaterUtc"/> never decreases,
/// whatever the OS clock does, and the persisted mark survives a restart.
/// </summary>
public sealed class MonotonicClockTests
{
    private static readonly DateTimeOffset Base = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task EarlierObserve_NeverLowersTheHighWater()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.InitializeAsync();

        var clock = fixture.Services.GetRequiredService<IMonotonicClock>();
        clock.Observe(Base.AddHours(5));
        var high = clock.HighWaterUtc;

        clock.Observe(Base); // an earlier instant is ignored, never applied.
        Assert.Equal(high, clock.HighWaterUtc);

        clock.Observe(Base.AddHours(6));
        Assert.Equal(Base.AddHours(6), clock.HighWaterUtc);
    }

    [Fact]
    public async Task HighWater_SurvivesANewContext()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.InitializeAsync();

        var clock = fixture.Services.GetRequiredService<IMonotonicClock>();
        clock.Observe(Base.AddHours(9));
        Assert.Equal(Base.AddHours(9), clock.HighWaterUtc);

        // Read the persisted row back with a brand-new context: the mark is on disk.
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var persisted = await db.Set<ClockHighWater>().AsNoTracking()
            .Where(e => e.Id == ClockHighWater.SingletonId)
            .Select(e => e.HighWaterUtc)
            .FirstAsync();
        Assert.Equal(Base.AddHours(9), persisted);
    }
}
