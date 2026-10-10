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
        foreach (var resource in resources.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var key = $"{db.CurrentTenantId}:{resource}";
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
        }
    }
}
