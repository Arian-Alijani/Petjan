using Animora.Application.Ports;
using Animora.Application.Text;
using Animora.Application.UseCases.Purchasing.ReadModels;
using Animora.Domain.Purchasing;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Queries;

/// <summary>
/// Supplier directory + recent goods-receipt reads for the «خرید و تأمین» tab. Tenant / soft-delete filters
/// are applied by <c>ModelConventions</c>. Receipts are read as aggregates (lines are auto-included) over a
/// bounded window, so the per-receipt cost total is exact without translating nested money projections.
/// </summary>
public sealed class PurchaseDirectoryQueries(AnimoraLocalDbContext db) : IPurchaseDirectoryQueries
{
    public async Task<IReadOnlyList<SupplierRowReadModel>> ListSuppliersAsync(string? search, int take, CancellationToken ct)
    {
        var q = db.Set<Supplier>().AsNoTracking().Where(s => s.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = "%" + PersianText.NormalizeDigits(search.Trim()) + "%";
            q = q.Where(s => EF.Functions.Like(s.Name, term) || EF.Functions.Like(s.Code, term)
                || (s.Phone != null && EF.Functions.Like(s.Phone, term)));
        }

        return await q.OrderBy(s => s.Name).ThenBy(s => s.Id).Take(take)
            .Select(s => new SupplierRowReadModel(s.Id, s.Code, s.Name, s.Phone, s.PaymentTermDays))
            .ToListAsync(ct);
    }

    public Task<int> CountSuppliersAsync(CancellationToken ct) =>
        db.Set<Supplier>().AsNoTracking().CountAsync(s => s.IsActive, ct);

    public Task<int> CountReceiptsAsync(CancellationToken ct) =>
        db.Set<GoodsReceipt>().AsNoTracking().CountAsync(ct);

    public async Task<IReadOnlyList<SupplierSummaryReadModel>> ListSupplierSummariesAsync(
        string? search, int take, CancellationToken ct)
    {
        var suppliers = await FilterSuppliers(search)
            .OrderBy(s => s.Name).ThenBy(s => s.Id)
            .Take(take)
            .Select(s => new { s.Id, s.Code, s.Name, s.Phone, s.PaymentTermDays })
            .ToListAsync(ct);

        if (suppliers.Count == 0)
        {
            return [];
        }

        var ids = suppliers.Select(s => s.Id).ToArray();
        var stats = await BuildStatsAsync(ids, ct);

        var rows = new List<SupplierSummaryReadModel>(suppliers.Count);
        foreach (var s in suppliers)
        {
            var stat = stats.GetValueOrDefault(s.Id);
            rows.Add(new SupplierSummaryReadModel(
                s.Id, s.Code, s.Name, s.Phone, s.PaymentTermDays,
                stat?.ReceiptCount ?? 0, stat?.TotalPurchasedMinor ?? 0L,
                stat?.LastPurchaseUtc, stat?.OpenOrderCount ?? 0));
        }

        return rows;
    }

    public async Task<SupplierSummaryReadModel?> FindSupplierSummaryAsync(string supplierId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(supplierId))
        {
            return null;
        }

        var supplier = await db.Set<Supplier>().AsNoTracking()
            .Where(s => s.Id == supplierId && s.IsActive)
            .Select(s => new { s.Id, s.Code, s.Name, s.Phone, s.PaymentTermDays })
            .FirstOrDefaultAsync(ct);
        if (supplier is null)
        {
            return null;
        }

        var stats = await BuildStatsAsync([supplier.Id], ct);
        var stat = stats.GetValueOrDefault(supplier.Id);
        return new SupplierSummaryReadModel(
            supplier.Id, supplier.Code, supplier.Name, supplier.Phone, supplier.PaymentTermDays,
            stat?.ReceiptCount ?? 0, stat?.TotalPurchasedMinor ?? 0L, stat?.LastPurchaseUtc, stat?.OpenOrderCount ?? 0);
    }

    public async Task<IReadOnlyList<SupplierPurchaseRecordReadModel>> ListSupplierPurchasesAsync(
        string supplierId, int take, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(supplierId))
        {
            return [];
        }

        // Posted receipts only (a draft/reversed receipt is not a completed purchase), newest first, over a
        // bounded window; lines are auto-included so the per-receipt cost total stays exact.
        var receipts = await db.Set<GoodsReceipt>().AsNoTracking()
            .Where(r => r.SupplierId == supplierId && r.State == GoodsReceiptState.Posted)
            .OrderByDescending(r => r.ReceivedUtc).ThenByDescending(r => r.Id)
            .Take(take)
            .ToListAsync(ct);

        var rows = new List<SupplierPurchaseRecordReadModel>(receipts.Count);
        foreach (var r in receipts)
        {
            var total = 0L;
            foreach (var line in r.Lines) total += line.LineCost.Minor;
            rows.Add(new SupplierPurchaseRecordReadModel(
                r.Id, r.ReceiptNumber, r.ReceivedUtc, r.Lines.Count, total, r.InvoiceReference));
        }

        return rows;
    }

    // Active suppliers, optionally filtered by name / code / phone (normalized digits).
    private IQueryable<Supplier> FilterSuppliers(string? search)
    {
        var q = db.Set<Supplier>().AsNoTracking().Where(s => s.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = "%" + PersianText.NormalizeDigits(search.Trim()) + "%";
            q = q.Where(s => EF.Functions.Like(s.Name, term) || EF.Functions.Like(s.Code, term)
                || (s.Phone != null && EF.Functions.Like(s.Phone, term)));
        }

        return q;
    }

    /// <summary>
    /// Per-supplier purchase statistics for a bounded id set, in two grouped reads (receipts + open orders),
    /// joined in memory — so a supplier row never issues its own query (avoids an N+1).
    /// </summary>
    private async Task<Dictionary<string, SupplierStat>> BuildStatsAsync(string[] supplierIds, CancellationToken ct)
    {
        var posted = await db.Set<GoodsReceipt>().AsNoTracking()
            .Where(r => r.State == GoodsReceiptState.Posted && supplierIds.Contains(r.SupplierId))
            .Select(r => new { r.SupplierId, r.ReceivedUtc, Lines = r.Lines.Select(l => new { l.Quantity, l.UnitCost }) })
            .ToListAsync(ct);

        var stats = new Dictionary<string, SupplierStat>(StringComparer.Ordinal);
        foreach (var r in posted)
        {
            var total = 0L;
            foreach (var line in r.Lines) total += line.UnitCost.ApplyPercent(line.Quantity.Value * 100m).Minor;

            var stat = stats.TryGetValue(r.SupplierId, out var existing) ? existing : new SupplierStat();
            stat.ReceiptCount++;
            stat.TotalPurchasedMinor += total;
            if (stat.LastPurchaseUtc is null || r.ReceivedUtc > stat.LastPurchaseUtc)
            {
                stat.LastPurchaseUtc = r.ReceivedUtc;
            }

            stats[r.SupplierId] = stat;
        }

        var openOrders = await db.Set<PurchaseOrder>().AsNoTracking()
            .Where(o => supplierIds.Contains(o.SupplierId)
                && (o.State == PurchaseOrderState.Submitted || o.State == PurchaseOrderState.PartiallyReceived))
            .GroupBy(o => o.SupplierId)
            .Select(g => new { SupplierId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        foreach (var o in openOrders)
        {
            var stat = stats.TryGetValue(o.SupplierId, out var existing) ? existing : new SupplierStat();
            stat.OpenOrderCount = o.Count;
            stats[o.SupplierId] = stat;
        }

        return stats;
    }

    private sealed class SupplierStat
    {
        public int ReceiptCount { get; set; }

        public long TotalPurchasedMinor { get; set; }

        public DateTimeOffset? LastPurchaseUtc { get; set; }

        public int OpenOrderCount { get; set; }
    }

    public async Task<IReadOnlyList<GoodsReceiptRowReadModel>> ListReceiptsAsync(int take, CancellationToken ct)
    {
        var receipts = await db.Set<GoodsReceipt>().AsNoTracking()
            .OrderByDescending(r => r.ReceivedUtc).ThenByDescending(r => r.Id)
            .Take(take)
            .ToListAsync(ct);

        var supplierIds = receipts.Select(r => r.SupplierId).Distinct(StringComparer.Ordinal).ToArray();
        var names = await db.Set<Supplier>().AsNoTracking()
            .Where(s => supplierIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Name })
            .ToDictionaryAsync(s => s.Id, s => s.Name, StringComparer.Ordinal, ct);

        var rows = new List<GoodsReceiptRowReadModel>(receipts.Count);
        foreach (var r in receipts)
        {
            var total = 0L;
            foreach (var line in r.Lines) total += line.LineCost.Minor;
            rows.Add(new GoodsReceiptRowReadModel(
                r.Id, r.ReceiptNumber, names.GetValueOrDefault(r.SupplierId, r.SupplierId), r.ReceivedUtc,
                r.Lines.Count, total, r.InvoiceReference, (int)r.State));
        }

        return rows;
    }
}
