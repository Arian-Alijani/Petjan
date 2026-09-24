using Animora.Domain.Inventory;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Queries;

/// <summary>
/// Derives stock on hand from the append-only <see cref="StockMovement"/> ledger. Absolute stock is never persisted.
/// </summary>
public sealed class StockOnHandQuery(AnimoraLocalDbContext db)
{
    /// <summary>Maximum product ids accepted per call.</summary>
    public const int MaxProductIds = 500;

    /// <summary>
    /// One grouped SQL <c>SUM(QuantityScaled)</c> over tenant-filtered movements by <c>(ProductId, VariantId)</c>.
    /// Movement quantities are already signed, so the sum is the on-hand quantity (exact INTEGER ×1000).
    /// </summary>
    /// <param name="productIds">Products to include (1…<see cref="MaxProductIds"/>, distinct).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>On-hand quantity per <c>(ProductId, VariantId)</c>; pairs without movements are absent.</returns>
    public async Task<IReadOnlyDictionary<(string ProductId, string VariantId), decimal>> GetAsync(
        IReadOnlyCollection<string> productIds, CancellationToken ct = default)
    {
        var ids = QueryGuards.Ids(productIds, MaxProductIds, nameof(productIds));
        var rows = await db.Set<StockMovement>().AsNoTracking()
            .Where(m => ids.Contains(m.ProductId))
            .GroupBy(m => new { m.ProductId, m.VariantId })
            .Select(g => new { g.Key.ProductId, g.Key.VariantId, OnHand = g.Sum(m => m.Quantity.Value) })
            .ToListAsync(ct);
        return rows.ToDictionary(r => (r.ProductId, r.VariantId), r => r.OnHand);
    }
}
