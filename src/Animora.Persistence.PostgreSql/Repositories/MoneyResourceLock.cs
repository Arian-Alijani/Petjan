using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Repositories;

/// <summary>Transaction-scoped resource locks, always tenant-qualified and acquired in sorted order.</summary>
public sealed class MoneyResourceLock(AnimoraServerDbContext db)
{
    public async Task AcquireAsync(IEnumerable<string> resources, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Financial locks require the command transaction.");
        // Two int4 keys (tenant, resource) instead of one 64-bit hash of the concatenation: a collision now
        // needs BOTH 32-bit hashes to match, so unrelated resources (and especially other tenants) are no longer
        // falsely serialised. Sorted-order acquisition is kept, which is what prevents lock-order deadlocks.
        var tenantId = db.CurrentTenantId;
        foreach (var resource in resources.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtext({tenantId}), hashtext({resource}))", ct);
        }
    }
}
