using Animora.Application.Ports;
using Animora.Application.UseCases.Purchasing.ReadModels;
using Animora.Domain.Purchasing;
using Animora.Domain.ValueObjects;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Queries;

/// <summary>
/// Grouped purchasing projections (C06). The screen snapshot is computed in three SELECTs:
/// (1) the purchase-order list (also the source of the active-order count and the supplier-debt
/// aggregate, derived in memory over the bounded list), (2) the month-to-date spend over posted
/// goods receipts, and (3) the supplier names for the listed orders. Tenant and soft-delete filters
/// are applied automatically by <c>ModelConventions</c>.
/// </summary>
public sealed class PurchasingQueries(AnimoraLocalDbContext db) : IPurchasingQueries
{
    private const int MaxOrders = 200;

    public async Task<PurchasingSnapshotReadModel> GetSnapshotAsync(DateTimeOffset nowUtc, CancellationToken ct)
    {
        // SELECT 1 — the recent purchase orders (bounded, newest first).
        var orders = await db.Set<PurchaseOrder>().AsNoTracking()
            .OrderByDescending(o => o.OrderedUtc).ThenByDescending(o => o.Id)
            .Take(MaxOrders)
            .Select(o => new
            {
                o.Id,
                o.OrderNumber,
                o.SupplierId,
                o.OrderedUtc,
                LineCount = o.Lines.Count,
                o.GrandTotal,
                State = (int)o.State,
            })
            .ToListAsync(ct);

        // SELECT 2 — supplier names for the listed orders.
        var supplierIds = orders.Select(o => o.SupplierId).Distinct(StringComparer.Ordinal).ToArray();
        var suppliers = await db.Set<Supplier>().AsNoTracking()
            .Where(s => supplierIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Name, s.PaymentTermDays })
            .ToListAsync(ct);
        var supplierById = new Dictionary<string, (string Name, int Term)>(suppliers.Count, StringComparer.Ordinal);
        for (var i = 0; i < suppliers.Count; i++)
        {
            supplierById[suppliers[i].Id] = (suppliers[i].Name, suppliers[i].PaymentTermDays);
        }

        // SELECT 3 — month-to-date spend: posted goods-receipt line cost this calendar month.
        var monthStart = new DateTimeOffset(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var mtdReceipts = await db.Set<GoodsReceipt>().AsNoTracking()
            .Where(r => r.State == GoodsReceiptState.Posted && r.ReceivedUtc >= monthStart && r.ReceivedUtc <= nowUtc)
            .SelectMany(r => r.Lines)
            .Select(l => new { l.Quantity, l.UnitCost })
            .ToListAsync(ct);
        var mtdSpend = 0L;
        for (var i = 0; i < mtdReceipts.Count; i++)
        {
            mtdSpend += mtdReceipts[i].UnitCost.ApplyPercent(mtdReceipts[i].Quantity.Value * 100m).Minor;
        }

        var currency = orders.Count > 0 ? orders[0].GrandTotal.Currency : CurrencyScale.Irt;
        var rows = new List<PurchaseOrderRowReadModel>(orders.Count);
        var activeCount = 0;
        var supplierDebt = 0L;
        for (var i = 0; i < orders.Count; i++)
        {
            var o = orders[i];
            var meta = supplierById.TryGetValue(o.SupplierId, out var m) ? m : (o.SupplierId, 0);
            rows.Add(new PurchaseOrderRowReadModel(
                o.Id, o.OrderNumber, meta.Item1, o.OrderedUtc, o.LineCount,
                o.GrandTotal.Minor, o.GrandTotal.Currency, meta.Item2, o.State));

            // Active = submitted or partially received (goods still expected).
            if (o.State is (int)PurchaseOrderState.Submitted or (int)PurchaseOrderState.PartiallyReceived)
            {
                activeCount++;
            }
            // Outstanding supplier debt: value of goods already received but not yet closed.
            if (o.State is (int)PurchaseOrderState.PartiallyReceived or (int)PurchaseOrderState.Received)
            {
                supplierDebt += o.GrandTotal.Minor;
            }
        }

        return new PurchasingSnapshotReadModel(activeCount, mtdSpend, supplierDebt, currency, rows);
    }
}
