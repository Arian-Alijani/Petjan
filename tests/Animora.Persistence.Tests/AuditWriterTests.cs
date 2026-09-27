using Animora.Application.Auditing;
using Animora.Domain.Primitives;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Persistence.Tests;

public sealed class AuditWriterTests
{
    [Fact]
    public async Task WriteIsDurableAppendOnlyAndContainsNoPiiColumns()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        using var scope = fixture.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<IAuditWriter>();
        var operationId = IdGenerator.NewId(fixture.Clock.UtcNow);
        var record = new AuditRecord(fixture.TenantId, "U-1", "D-1", fixture.Clock.UtcNow,
            AuditCategory.Maintenance, "test.audit", null, null, operationId, operationId,
            AuditOutcome.Success, null, null, null, null, null);

        Assert.True((await writer.WriteAsync(record)).IsSuccess);
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var row = await db.Set<AuditEntry>().AsTracking().SingleAsync();
        db.Entry(row).State = EntityState.Modified;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        row = await db.Set<AuditEntry>().AsTracking().SingleAsync();
        db.Remove(row);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());

        var names = typeof(AuditEntry).GetProperties().Select(property => property.Name).ToArray();
        Assert.DoesNotContain(names, name => name.Contains("Phone", StringComparison.OrdinalIgnoreCase)
            || name.Contains("OwnerName", StringComparison.OrdinalIgnoreCase)
            || name.Contains("PetName", StringComparison.OrdinalIgnoreCase));
    }
}
