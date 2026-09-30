using Animora.Application.Time;
using Animora.Domain.Finance;
using Animora.Domain.Inventory;
using Animora.Domain.Sales;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Finance;

/// <summary>
/// Recomputes the three rebuildable finance facts from the authoritative tables — the append-only
/// invoices/payments/movements (and posted expenses) that are the real record. The facts are a
/// "cache with a proof": the live path (<see cref="FinanceProjectionStore"/>, driven by the outbox
/// worker) keeps them warm, and this rebuild can always reproduce them exactly. Never touches the
/// live query path (A-RULE 034) — it writes facts, it does not read them.
/// </summary>
/// <remarks>
/// The rebuild runs entirely on the caller's <see cref="AnimoraLocalDbContext"/> and, when called
/// from <c>LocalStoreMaintenance</c>, inside a transaction that first deletes the tenant's fact rows.
/// Day bucketing uses the clinic time zone here (never in SQL — CONVENTIONS §5). Amounts are stored
/// in minor units (R-B24 — never a decimal); COGS is the weighted-average extended cost of a
/// <see cref="MovementKind.Sale"/> movement.
/// </remarks>
public static class FinanceProjectionRebuilder
{
    /// <summary>Deletes the tenant's fact rows and recomputes them. Returns the number of rows written.</summary>
    public static async Task<int> RebuildAsync(
        AnimoraLocalDbContext db, TimeZoneInfo zone, string tenantId, CancellationToken ct)
    {
        await db.Set<DailyFinanceFact>().Where(f => f.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await db.Set<ProductSalesFact>().Where(f => f.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await db.Set<OwnerFinancialSnapshot>().Where(f => f.TenantId == tenantId).ExecuteDeleteAsync(ct);

        var daily = new Dictionary<int, Daily>();
        var product = new Dictionary<(int Day, string ProductId, string VariantId), ProductAcc>();
        var owner = new Dictionary<string, OwnerAcc>();

        // 1) Invoices: revenue/discount/insurance/count + per-line product revenue + owner spend.
        var invoices = await db.Set<Invoice>().AsNoTracking()
            .Where(i => i.State != InvoiceState.Draft && i.State != InvoiceState.Void && i.PostedUtc != null)
            .Select(i => new
            {
                i.Id, i.OwnerId, i.State, i.PostedUtc, i.SettledUtc,
                Subtotal = i.Subtotal.Minor, Discount = i.DiscountTotal.Minor,
                Insurance = i.InsuranceCoverage.Minor, Grand = i.GrandTotal.Minor,
            })
            .ToListAsync(ct);

        for (var i = 0; i < invoices.Count; i++)
        {
            var inv = invoices[i];
            var day = LocalDay(inv.PostedUtc!.Value, zone);
            var row = For(daily, day);
            row.Revenue += inv.Subtotal;
            row.Discount += inv.Discount;
            row.Insurance += inv.Insurance;
            row.Invoices += 1;

            if (inv.State == InvoiceState.Paid && inv.SettledUtc is { } settled)
                For(daily, LocalDay(settled, zone)).PaidInvoices += 1;

            if (inv.OwnerId is { Length: > 0 } ownerId)
            {
                var acc = For(owner, ownerId);
                acc.Spend += inv.Grand;
                acc.Invoices += 1;
                if (acc.LastInvoiceUtc is null || inv.PostedUtc > acc.LastInvoiceUtc) acc.LastInvoiceUtc = inv.PostedUtc;
            }
        }

        // 2) Posted invoice lines: per-product gross revenue and quantity sold.
        var lines = await db.Set<Invoice>().AsNoTracking()
            .Where(i => i.State != InvoiceState.Draft && i.State != InvoiceState.Void && i.PostedUtc != null)
            .SelectMany(i => i.Lines, (i, l) => new
            {
                i.PostedUtc, l.ProductId, l.VariantId, l.Kind,
                Quantity = l.Quantity.Value, Gross = l.LineGross.Minor,
            })
            .Where(x => x.Kind == InvoiceLineKind.Product && x.ProductId != null)
            .ToListAsync(ct);

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var day = LocalDay(line.PostedUtc!.Value, zone);
            var acc = For(product, (day, line.ProductId!, line.VariantId ?? string.Empty));
            acc.Quantity += line.Quantity;
            acc.Revenue += line.Gross;
        }

        // 3) Sale movements: cost of goods sold per day and per product.
        var movements = await db.Set<StockMovement>().AsNoTracking()
            .Where(m => m.Kind == MovementKind.Sale)
            .Select(m => new { m.ProductId, m.VariantId, m.OccurredUtc, Qty = m.Quantity.Value, Cost = m.UnitCost.Minor })
            .ToListAsync(ct);

        for (var i = 0; i < movements.Count; i++)
        {
            var m = movements[i];
            var cost = checked((long)decimal.Round(Math.Abs(m.Qty) * m.Cost, 0, MidpointRounding.AwayFromZero));
            var day = LocalDay(m.OccurredUtc, zone);
            For(daily, day).Cogs += cost;
            For(product, (day, m.ProductId, m.VariantId)).Cogs += cost;
        }

        // 4) Posted expenses: operating cost per day.
        var expenses = await db.Set<Expense>().AsNoTracking()
            .Where(e => e.State != ExpenseState.Draft)
            .Select(e => new { e.IncurredUtc, Amount = e.Amount.Minor })
            .ToListAsync(ct);
        for (var i = 0; i < expenses.Count; i++)
            For(daily, LocalDay(expenses[i].IncurredUtc, zone)).Expense += Math.Abs(expenses[i].Amount);

        // 5) Negative payments: money given back grouped by the day it left.
        var refunds = await db.Set<Payment>().AsNoTracking()
            .Where(p => p.Amount.Minor < 0)
            .Select(p => new { p.ReceivedUtc, Amount = p.Amount.Minor })
            .ToListAsync(ct);
        for (var i = 0; i < refunds.Count; i++)
            For(daily, LocalDay(refunds[i].ReceivedUtc, zone)).Refund += Math.Abs(refunds[i].Amount);

        // ── materialize ──────────────────────────────────────────────────────
        var written = 0;
        var dailySet = db.Set<DailyFinanceFact>();
        foreach (var (day, acc) in daily)
        {
            var fact = DailyFinanceFact.Create(tenantId, day);
            fact.Apply(acc.Revenue, acc.Cogs, acc.Expense, acc.Discount, acc.Insurance, acc.Refund,
                acc.Invoices, acc.PaidInvoices);
            dailySet.Add(fact);
            written++;
        }

        var productSet = db.Set<ProductSalesFact>();
        foreach (var ((day, productId, variantId), acc) in product)
        {
            var fact = ProductSalesFact.Create(tenantId, day, productId, variantId);
            fact.Apply(acc.Quantity, acc.Revenue, acc.Cogs);
            productSet.Add(fact);
            written++;
        }

        var ownerSet = db.Set<OwnerFinancialSnapshot>();
        foreach (var (ownerId, acc) in owner)
        {
            var snapshot = OwnerFinancialSnapshot.Create(tenantId, ownerId);
            snapshot.Apply(acc.Spend, acc.LastInvoiceUtc, acc.Invoices);
            ownerSet.Add(snapshot);
            written++;
        }

        return written;
    }

    /// <summary>The clinic-local day (<c>yyyyMMdd</c>) an instant falls on.</summary>
    public static int LocalDay(DateTimeOffset utc, TimeZoneInfo zone)
    {
        var day = ClinicCalendar.LocalDay(utc.UtcTicks, zone);
        return day.Year * 10000 + day.Month * 100 + day.Day;
    }

    private static Daily For(Dictionary<int, Daily> map, int day)
    {
        if (map.TryGetValue(day, out var existing)) return existing;
        var created = new Daily();
        map[day] = created;
        return created;
    }

    private static ProductAcc For(Dictionary<(int, string, string), ProductAcc> map, (int, string, string) key)
    {
        if (map.TryGetValue(key, out var existing)) return existing;
        var created = new ProductAcc();
        map[key] = created;
        return created;
    }

    private static OwnerAcc For(Dictionary<string, OwnerAcc> map, string ownerId)
    {
        if (map.TryGetValue(ownerId, out var existing)) return existing;
        var created = new OwnerAcc();
        map[ownerId] = created;
        return created;
    }

    private sealed class Daily
    {
        public long Revenue;
        public long Cogs;
        public long Expense;
        public long Discount;
        public long Insurance;
        public long Refund;
        public int Invoices;
        public int PaidInvoices;
    }

    private sealed class ProductAcc
    {
        public decimal Quantity;
        public long Revenue;
        public long Cogs;
    }

    private sealed class OwnerAcc
    {
        public long Spend;
        public int Invoices;
        public DateTimeOffset? LastInvoiceUtc;
    }
}
