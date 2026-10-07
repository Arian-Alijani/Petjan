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
