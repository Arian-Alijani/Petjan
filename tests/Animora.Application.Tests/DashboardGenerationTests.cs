using System;
using System.Linq;
using System.Threading.Tasks;
using Animora.Application.Security;
using Animora.Client.Services.Dashboard;
using Animora.Contracts.Navigation;
using Animora.Domain.Identity;
using Animora.Domain.Patients;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Animora.Persistence.Sqlite.Queries;
using Animora.Persistence.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Application.Tests;

/// <summary>D19/D22: the cache version is a tenant source generation advanced by every committed source write.</summary>
public sealed class DashboardGenerationTests
{
    private static async Task<string> VersionAsync(LocalStoreTestFixture fixture, string? tenant = null)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        return await new DashboardQueries(db).VersionAsync(tenant ?? fixture.TenantId, default);
    }

    [Fact]
    public async Task Generation_AdvancesOnFactsAndOltpWrites_WithoutAnyActivityEvent_AndIsTenantScoped()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        Assert.Equal("0", await VersionAsync(fixture));

        // A projection-worker style fact write: no ActivityEvent row exists, the old probe could not see this.
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var fact = DailyFinanceFact.Create(fixture.TenantId, 20260301);
            fact.Apply(revenue: 100);
            db.Add(fact);
            await db.SaveChangesAsync();
            Assert.Empty(await db.Set<ActivityEvent>().ToListAsync());
        }
        var afterFact = await VersionAsync(fixture);
        Assert.NotEqual("0", afterFact);

        // An OLTP change with no feed row (patient registration) also advances it.
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var owner = Owner.Register(fixture.TenantId, "مالک", "09120000001", fixture.Clock);
            db.AddRange(owner, Pet.Register(fixture.TenantId, owner.Id, "بیمار", PetSpecies.Cat, fixture.Clock));
            await db.SaveChangesAsync();
        }
        var afterPatients = await VersionAsync(fixture);
        Assert.True(long.Parse(afterPatients) > long.Parse(afterFact));

        // Another tenant is isolated, and a read does not move the generation.
        Assert.Equal("0", await VersionAsync(fixture, "OTHER-TENANT"));
        Assert.Equal(afterPatients, await VersionAsync(fixture));
    }

    [Fact]
    public async Task Generation_IsNotAdvancedByARolledBackWrite_ButByAFactUpdate()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        var before = await VersionAsync(fixture);
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            var fact = DailyFinanceFact.Create(fixture.TenantId, 20260301);
            fact.Apply(revenue: 5);
            db.Add(fact);
            await db.SaveChangesAsync();
            await transaction.RollbackAsync();
        }
        Assert.Equal(before, await VersionAsync(fixture));

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var fact = DailyFinanceFact.Create(fixture.TenantId, 20260301);
            fact.Apply(revenue: 5);
            db.Add(fact);
            await db.SaveChangesAsync();
        }
        var inserted = await VersionAsync(fixture);
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var fact = await db.Set<DailyFinanceFact>().AsTracking().SingleAsync();
            fact.Apply(revenue: 7); // the late, in-place projection update
            await db.SaveChangesAsync();
        }
        Assert.True(long.Parse(await VersionAsync(fixture)) > long.Parse(inserted));
    }

    [Fact]
    public async Task Service_FactChangedWithoutNewEvent_InvalidatesTheCache_ButUnchangedDataStillHits()
    {
        await using var host = await DashboardAuthorizationTests.CreateHostAsync(StaffRole.Manager);
        var service = new DashboardQueryService(host.Services.GetRequiredService<IServiceScopeFactory>());
        var first = await service.GetSnapshotAsync(UserRole.Manager);
        Assert.Same(first, await service.GetSnapshotAsync(UserRole.Manager)); // no change → cache hit

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var fact = await db.Set<DailyFinanceFact>().AsTracking().SingleAsync(x => x.LocalDay == 20260924);
            fact.Apply(revenue: 1000); // worker-style update after the source commit; no ActivityEvent is written
            await db.SaveChangesAsync();
            Assert.Empty(await db.Set<ActivityEvent>().ToListAsync());
        }
        var refreshed = await service.GetSnapshotAsync(UserRole.Manager);
        Assert.NotSame(first, refreshed);
        Assert.NotEqual(first.Stats[0].Value, refreshed.Stats[0].Value);
        Assert.Equal("13,345", refreshed.Stats[0].Value);
    }

    [Fact]
    public async Task Snapshot_ReportsProjectionLag_AndFailedProjection_SoStaleFactsAreNeverPresentedAsReady()
    {
        await using var host = await DashboardAuthorizationTests.CreateHostAsync(StaffRole.Manager);
        var service = new DashboardQueryService(host.Services.GetRequiredService<IServiceScopeFactory>());
        var clean = await service.GetSnapshotAsync(UserRole.Manager);
        Assert.NotNull(clean.Freshness);
        Assert.True(clean.Freshness!.IsProjectionCurrent);
        Assert.Equal(host.Clock.UtcNow, clean.Freshness.GeneratedAtUtc);

        async Task InsertOutboxAsync(string id, int status)
        {
            using var scope = host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO OutboxMessages (Id, MessageType, PayloadJson, Status, CreatedAtUtc, Attempts) VALUES ({0}, 'X', '{{}}', {1}, 0, 0)",
                id, status);
            // Outbox rows are not a dashboard source table: advance the generation like the real worker's fact write.
            var fact = await db.Set<DailyFinanceFact>().AsTracking().FirstAsync();
            fact.Apply(revenue: 1);
            await db.SaveChangesAsync();
        }

        await InsertOutboxAsync("OB-PENDING", (int)OutboxStatus.Pending);
        var lagging = await service.GetSnapshotAsync(UserRole.Manager);
        Assert.Equal(1, lagging.Freshness!.PendingProjectionCount);
        Assert.False(lagging.Freshness.IsProjectionCurrent);

        await InsertOutboxAsync("OB-DEAD", (int)OutboxStatus.DeadLettered);
        var failed = await service.GetSnapshotAsync(UserRole.Manager);
        Assert.True(failed.Freshness!.ProjectionFailed);
        Assert.Equal(1, failed.Freshness.PendingProjectionCount);

        // A role without finance data carries no projection claim at all.
        var doctor = await DashboardAuthorizationTests.CreateHostAsync(StaffRole.Doctor);
        await using (doctor)
            Assert.Null((await new DashboardQueryService(doctor.Services.GetRequiredService<IServiceScopeFactory>())
                .GetSnapshotAsync(UserRole.Doctor)).Freshness);
    }
}
