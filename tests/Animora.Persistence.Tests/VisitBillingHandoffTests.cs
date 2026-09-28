using Animora.Domain.Clinical;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Persistence.Tests;

public sealed class VisitBillingHandoffTests
{
    [Fact]
    public async Task Migration010_UpgradesDatabaseAt009_AndEnforcesUniqueVisit()
    {
        using var fixture = new LocalStoreTestFixture();
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var migrator = db.Database.GetService<IMigrator>();
        await migrator.MigrateAsync("20260927120000_009_PatientsSearchKeys");
        Assert.DoesNotContain(await db.Database.GetAppliedMigrationsAsync(), m => m.EndsWith("010_C03Schema"));
        await db.Database.MigrateAsync();
        Assert.Contains(await db.Database.GetAppliedMigrationsAsync(), m => m.EndsWith("010_C03Schema"));

        var at = fixture.Clock.UtcNow;
        db.Add(VisitBillingHandoff.Create(fixture.TenantId, "VISIT-1", "OWNER-1", "PET-1", "U-1", at, "[]"));
        await db.SaveChangesAsync();
        db.Add(VisitBillingHandoff.Create(fixture.TenantId, "VISIT-1", "OWNER-1", "PET-1", "U-1", at, "[]"));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task MedicalHistoryEntry_CannotBeUpdated()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var history = new MedicalHistoryEntry(fixture.TenantId, "PET-1", MedicalHistoryKind.Visit,
            "Exam", fixture.Clock.UtcNow, "U-1", fixture.Clock);
        db.Add(history);
        await db.SaveChangesAsync();
        db.Entry(history).State = EntityState.Modified;
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        Assert.Contains("Append-only", ex.Message);
    }
}
