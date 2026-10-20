using System.Text.Json;
using Animora.Application.Time;
using Animora.Contracts.Wire.Sync;
using Animora.Domain.Finance;
using Animora.Domain.Inventory;
using Animora.Domain.Sales;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Finance;

/// <summary>
/// Recomputes the three rebuildable finance facts from the authoritative tables — the append-only
/// invoices/payments/movements (and posted expenses) that are the real record, PLUS the read-only
/// <see cref="PeerInvoice"/>/<see cref="PeerPayment"/> cache of transactions other devices settled
/// (D04b). The facts are a "cache with a proof": the live path (<see cref="FinanceProjectionStore"/>,
/// driven by the outbox worker) keeps the locally-created rows warm, the peer-finance pull triggers a
/// rebuild for cross-device rows, and this method can always reproduce the whole set exactly. Never
/// touches the live query path (A-RULE 034) — it writes facts, it does not read them.
/// </summary>
/// <remarks>
/// The rebuild runs entirely on the caller's <see cref="AnimoraLocalDbContext"/> and, when called
/// from <c>LocalStoreMaintenance</c>, inside a transaction that first deletes the tenant's fact rows.
/// Day bucketing uses the clinic time zone here (never in SQL — CONVENTIONS §5). Amounts are stored
/// in minor units (R-B24 — never a decimal); COGS is the weighted-average extended cost of a
/// <see cref="MovementKind.Sale"/> movement.
/// <para>
/// <b>Peer dedup.</b> A peer invoice/payment whose id matches a local row is the SAME transaction
/// (invoice id == operation id), so only peer rows with no local twin are folded in — this device's
/// own invoices are counted once, from the authoritative local table. Peer products carry no local
/// <see cref="MovementKind.Sale"/> movement, so their COGS contribution is zero (honest: the stock
/// ledger belongs to the originating device); gross revenue and quantity still count in full.
/// </para>
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
        //    SAC-01: a posted-then-voided invoice is folded on its POST day like any posted invoice and then
        //    reversed on its VOID day (refund += grand total, revenue/discount/insurance reversed, count -1),
        //    exactly as FinanceProjectionStore's InvoiceVoidedEvent arm does on the warm path. The entity has no
        //    dedicated void timestamp; Void is terminal and only legal while nothing is paid, so UpdatedAt (stamped
        //    on every save) is the void instant. Never-posted (draft) voids contribute nothing.
        var invoices = await db.Set<Invoice>().AsNoTracking()
            .Where(i => i.State != InvoiceState.Draft && i.PostedUtc != null)
            .Select(i => new
            {
                i.Id, i.OwnerId, i.State, i.PostedUtc, i.SettledUtc, i.UpdatedAt,
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

            if (inv.State == InvoiceState.Void)
            {
                var voidRow = For(daily, LocalDay(inv.UpdatedAt, zone));
                voidRow.Revenue -= inv.Subtotal;
                voidRow.Discount -= inv.Discount;
                voidRow.Insurance -= inv.Insurance;
                voidRow.Refund += inv.Grand;
                voidRow.Invoices -= 1;
                if (inv.OwnerId is { Length: > 0 } voidOwner)
                {
                    var acc = For(owner, voidOwner);
                    acc.Spend -= inv.Grand;
                    acc.Invoices -= 1;
                }
            }
        }

        // 2) Posted invoice lines: per-product gross revenue and quantity sold.
        var lines = await db.Set<Invoice>().AsNoTracking()
            .Where(i => i.State != InvoiceState.Draft && i.PostedUtc != null)
            .SelectMany(i => i.Lines, (i, l) => new
            {
                i.PostedUtc, i.State, i.UpdatedAt, l.ProductId, l.VariantId, l.Kind,
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

            if (line.State == InvoiceState.Void)
            {
                // SAC-01: reverse on the void day, mirroring the live InvoiceVoidedEvent product arm.
                var voidAcc = For(product, (LocalDay(line.UpdatedAt, zone), line.ProductId!, line.VariantId ?? string.Empty));
                voidAcc.Quantity -= line.Quantity;
                voidAcc.Revenue -= line.Gross;
            }
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

        // 6) Peer finance (D04b): fold in transactions other devices settled, which reach this device
        //    only through the read-only GET /finance/ledger cache — never the command pipeline or the
        //    generic state feed. Without this, the «آمار»/داشبورد facts would reflect only this device's
        //    own invoices while the Sales list (which already unions the peer cache) shows everyone's.
        await FoldPeerFinanceAsync(db, zone, tenantId, daily, product, owner, ct);

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

    /// <summary>
    /// Folds the read-only peer-finance cache into the same accumulators the local tables filled. Peer rows that
    /// coincide with a local invoice (same id ⇒ same transaction) are skipped so this device's own invoices are
    /// never double-counted. Only posted, non-void peer invoices contribute revenue, mirroring the local filter.
    /// </summary>
    private static async Task FoldPeerFinanceAsync(
        AnimoraLocalDbContext db, TimeZoneInfo zone, string tenantId,
        Dictionary<int, Daily> daily,
        Dictionary<(int Day, string ProductId, string VariantId), ProductAcc> product,
        Dictionary<string, OwnerAcc> owner,
        CancellationToken ct)
    {
        // Ids of invoices this device already owns authoritatively: never fold the peer twin of a local invoice.
        var localInvoiceIds = (await db.Set<Invoice>().AsNoTracking()
                .Select(i => i.Id).ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        var peerInvoices = await db.Set<PeerInvoice>().AsNoTracking()
            .Where(i => i.TenantId == tenantId && i.PostedUtc != null
                && i.State != InvoiceState.Draft && i.State != InvoiceState.Void)
            .Select(i => new { i.Id, i.OwnerId, i.State, i.PostedUtc, i.GrandTotalMinor, i.DetailJson })
            .ToListAsync(ct);

        foreach (var inv in peerInvoices)
        {
            if (localInvoiceIds.Contains(inv.Id)) continue; // same transaction, already counted locally.

            FinanceInvoiceDetail? detail = null;
            if (inv.DetailJson is { Length: > 0 } json)
            {
                try { detail = JsonSerializer.Deserialize<FinanceInvoiceDetail>(json); }
                catch (JsonException) { detail = null; } // legacy/summary-only row: count totals, skip line split.
            }

            var day = LocalDay(inv.PostedUtc!.Value, zone);
            var row = For(daily, day);
            // Detail (when present) carries the economic split; otherwise the grand total is the only honest revenue figure.
            row.Revenue += detail?.SubtotalMinor ?? inv.GrandTotalMinor;
            row.Discount += detail?.DiscountMinor ?? 0;
            row.Insurance += detail?.InsuranceMinor ?? 0;
            row.Invoices += 1;
            if (inv.State == InvoiceState.Paid) row.PaidInvoices += 1;

            if (inv.OwnerId is { Length: > 0 } ownerId)
            {
                var acc = For(owner, ownerId);
                acc.Spend += inv.GrandTotalMinor;
                acc.Invoices += 1;
                if (acc.LastInvoiceUtc is null || inv.PostedUtc > acc.LastInvoiceUtc) acc.LastInvoiceUtc = inv.PostedUtc;
            }

            if (detail?.Lines is { Count: > 0 } lines)
                foreach (var line in lines)
                {
                    if (line.Kind != (int)InvoiceLineKind.Product || string.IsNullOrEmpty(line.ProductId)) continue;
                    var acc = For(product, (day, line.ProductId!, line.VariantId ?? string.Empty));
                    acc.Quantity += line.Quantity;
                    // LineGross = Quantity × UnitPrice, before discount/tax (matches the local path's l.LineGross.Minor).
                    acc.Revenue += checked((long)decimal.Round(line.Quantity * line.UnitPriceMinor, 0, MidpointRounding.AwayFromZero));
                    // COGS stays 0: the Sale stock movement lives on the originating device, not here.
                }
        }

        // Peer refunds: money handed back on another device, grouped by the day it left.
        var peerRefunds = await db.Set<PeerPayment>().AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.AmountMinor < 0
                && !db.Set<Payment>().Any(l => l.Id == p.Id))
            .Select(p => new { p.ReceivedUtc, p.AmountMinor })
            .ToListAsync(ct);
        for (var i = 0; i < peerRefunds.Count; i++)
            For(daily, LocalDay(peerRefunds[i].ReceivedUtc, zone)).Refund += Math.Abs(peerRefunds[i].AmountMinor);
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
