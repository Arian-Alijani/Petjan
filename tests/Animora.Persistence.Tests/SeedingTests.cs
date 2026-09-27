using System;
using System.Linq;
using System.Threading.Tasks;
using Animora.Domain.Boarding;
using Animora.Domain.Finance;
using Animora.Domain.Identity;
using Animora.Domain.Inventory;
using Animora.Domain.Patients;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Persistence.Tests;

/// <summary>
/// A10c reference seeding: idempotent on a second run, complete (tenant, permissions, roles, chart,
/// units, categories, rooms, one open period), all rows on the local default tenant, and the static
/// catalogs are deliberately not persisted.
/// </summary>
public sealed class SeedingTests
{
    [Fact]
    public async Task Seeding_IsIdempotentAndComplete()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();

        using (var scope = fixture.CreateScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<LocalSeeder>();
            Assert.True((await seeder.SeedAsync()).IsSuccess);
        }

        // A second run skips every applied block — no duplicate rows, no unique-index failure.
        using (var scope = fixture.CreateScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<LocalSeeder>();
            Assert.True((await seeder.SeedAsync()).IsSuccess);
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();

            Assert.Equal(1, await db.Set<Tenant>().CountAsync());
            Assert.Equal(PermissionCodes.All.Count, await db.Set<Permission>().CountAsync());
            Assert.Equal(Enum.GetValues<StaffRole>().Length, await db.Set<RoleDefinition>().CountAsync());
            Assert.Equal(ChartOfAccounts.Default.Count, await db.Set<LedgerAccount>().CountAsync());
            Assert.Equal(7, await db.Set<UnitOfMeasure>().CountAsync());
            Assert.Equal(5, await db.Set<ProductCategory>().CountAsync());
            Assert.Equal(8, await db.Set<Room>().CountAsync());
            Assert.Equal(1, await db.Set<AccountingPeriod>().CountAsync());
            // 8 reference blocks + the C01 patients.sample block (SeedSampleData defaults true).
            Assert.Equal(9, await db.Set<SeedMarker>().CountAsync());

            var period = await db.Set<AccountingPeriod>().SingleAsync();
            Assert.Equal(PeriodState.Open, period.State);

            // Catalog values are the authoritative ones.
            var codes = await db.Set<ProductCategory>().Select(c => c.Code).ToListAsync();
            Assert.Contains("MED", codes);
            Assert.Contains("SRV", codes);
            var rooms = await db.Set<Room>().Select(r => r.Code).ToListAsync();
            Assert.Contains("R-01", rooms);
            Assert.Contains("R-08", rooms);
        }
    }

    [Fact]
    public async Task SeededBusinessRows_CarryTheLocalTenant()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();

        using (var scope = fixture.CreateScope())
        {
            Assert.True((await scope.ServiceProvider.GetRequiredService<LocalSeeder>().SeedAsync()).IsSuccess);
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var accounts = await db.Set<LedgerAccount>().ToListAsync();
            Assert.NotEmpty(accounts);
            Assert.All(accounts, a => Assert.Equal(Tenant.LocalDefaultId, a.TenantId));
        }
    }

    [Fact]
    public async Task SampleData_SeedsTheC01OwnersPetsWeightsAndDocuments()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();

        using (var scope = fixture.CreateScope())
        {
            Assert.True((await scope.ServiceProvider.GetRequiredService<LocalSeeder>().SeedAsync()).IsSuccess);
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();

            // Phase: 12 owners, 18 pets, weight histories and 2 documents each.
            Assert.Equal(12, await db.Set<Owner>().CountAsync());
            Assert.Equal(18, await db.Set<Pet>().CountAsync());
            Assert.Equal(36, await db.Set<WeightEntry>().CountAsync());   // 2 per pet
            Assert.Equal(36, await db.Set<DocumentReference>().CountAsync()); // 2 per pet

            // Every seeded document points at a pet (and its owner) and carries a well-formed hash.
            var docs = await db.Set<DocumentReference>().AsNoTracking().ToListAsync();
            Assert.All(docs, d =>
            {
                Assert.False(string.IsNullOrWhiteSpace(d.PetId));
                Assert.False(string.IsNullOrWhiteSpace(d.OwnerId));
                Assert.Equal(64, d.ContentHashSha256.Length);
                Assert.Equal(d.ContentHashSha256, d.ContentHashSha256.ToLowerInvariant());
            });
        }
    }

    [Fact]
    public async Task Seeding_RefusesAForeignTenantContext()
    {
        using var fixture = new LocalStoreTestFixture(tenantId: "T-OTHER");
        await fixture.MigrateAsync();

        using var scope = fixture.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<LocalSeeder>().SeedAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(Animora.Domain.Results.ErrorCodes.ValidationFailed, result.Error);
    }
}
