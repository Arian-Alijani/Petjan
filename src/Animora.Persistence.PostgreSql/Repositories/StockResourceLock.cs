using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Repositories;

internal static class StockResourceLock
{
    public static async Task AcquireAsync(AnimoraServerDbContext db, string productId, string variantId, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Stock facts require the command transaction.");
        var resource = $"{db.CurrentTenantId}:stock:{productId}:{variantId}";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({resource}, 0))", ct);
    }
}
