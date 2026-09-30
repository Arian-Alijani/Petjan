using Animora.Domain.Inventory;
using Animora.Persistence.Sqlite.Converters;
using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Inventory;

/// <summary>
/// Keeps <see cref="StockOnHandSnapshot"/> in step with the append-only ledger (C06). Called from
/// <c>AnimoraLocalDbContext.PrepareSave</c>, so every <see cref="StockMovement"/> added through ANY write path
/// (receipts, adjustments, future C07 sales / D07 sync) updates its snapshot row in the SAME transaction.
/// </summary>
/// <remarks>
/// Movements are append-only (<c>AppendOnlyGuard</c>), so only <c>Added</c> rows can change on-hand. The
/// <paramref name="applied"/> set is owned by the context and cleared after a successful save: a failed
/// save that is retried on the same context never double-counts a movement.
/// </remarks>
internal static class StockSnapshotUpdater
{
    public static void Maintain(DbContext db, string currentTenantId, ISet<string> applied)
    {
        List<StockMovement>? added = null;
        foreach (var entry in db.ChangeTracker.Entries<StockMovement>())
        {
            if (entry.State != EntityState.Added || applied.Contains(entry.Entity.Id)) continue;
            (added ??= []).Add(entry.Entity);
        }
        if (added is null) return;

        var snapshots = db.Set<StockOnHandSnapshot>();
        var byKey = new Dictionary<(string Tenant, string Product, string Variant), StockOnHandSnapshot>();
        foreach (var local in snapshots.Local)
            byKey[(local.TenantId, local.ProductId, local.VariantId)] = local;

        // One round-trip for every key not already tracked.
        HashSet<string>? missingProducts = null;
        for (var i = 0; i < added.Count; i++)
        {
            var m = added[i];
            if (!byKey.ContainsKey((TenantOf(m, currentTenantId), m.ProductId, m.VariantId)))
                (missingProducts ??= new HashSet<string>(StringComparer.Ordinal)).Add(m.ProductId);
        }
        if (missingProducts is not null)
        {
            var tenantIds = added.Select(m => TenantOf(m, currentTenantId)).Distinct().ToArray();
            var loaded = snapshots.AsTracking()
                .Where(s => tenantIds.Contains(s.TenantId) && missingProducts.Contains(s.ProductId)).ToList();
            foreach (var row in loaded) byKey.TryAdd((row.TenantId, row.ProductId, row.VariantId), row);
        }

        for (var i = 0; i < added.Count; i++)
        {
            var m = added[i];
            var key = (TenantOf(m, currentTenantId), m.ProductId, m.VariantId);
            if (!byKey.TryGetValue(key, out var snapshot))
            {
                snapshot = StockOnHandSnapshot.Create(key.Item1, m.ProductId, m.VariantId);
                snapshots.Add(snapshot);
                byKey[key] = snapshot;
            }
            snapshot.Apply(FixedScaleQuantityConverter.ToScaled(m.Quantity.Value), m.OccurredUtc, m.Id);
            applied.Add(m.Id);
        }
    }

    private static string TenantOf(StockMovement movement, string currentTenantId) =>
        movement.TenantId.Length == 0 ? currentTenantId : movement.TenantId;
}
