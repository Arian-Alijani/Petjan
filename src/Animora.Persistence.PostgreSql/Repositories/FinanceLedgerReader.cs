using Animora.Application.Ports;
using Animora.Contracts.Wire.Sync;
using Animora.Contracts.Wire.Commands;
using Animora.Persistence.PostgreSql.Commands;
using Animora.Domain.Patients;
using Animora.Domain.Sales;
using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Repositories;

/// <summary>Recorded cancellation time, independent of the invoice's current transport timestamp.</summary>
internal sealed class FinanceVoidProjection
{
    public string InvoiceId { get; init; } = "";
    public DateTimeOffset? OccurredUtc { get; init; }

    internal static IQueryable<FinanceVoidProjection> Query(AnimoraServerDbContext db) =>
        db.Set<Invoice>().AsNoTracking().Where(i => i.State == InvoiceState.Void)
            .Select(i => new FinanceVoidProjection
            {
                InvoiceId = i.Id,
                // Retained receipts survive payload compaction. A zero-value or draft cancellation has no
                // credit note, so its accepted void receipt is essential. Posted voids may report the
                // generated adjustment id as slot zero instead of the invoice id.
                OccurredUtc = db.CommandIntake.Where(c => c.CommandType == CommandTypes.SalesInvoiceVoid
                        && c.Status == IntakeStatus.Settled && c.Outcome == (short)SettlementOutcome.Accepted
                        && (c.ServerEntityId == i.Id || i.Adjustments.Any(a => a.AdjustmentId == c.ServerEntityId)))
                    .Select(c => c.SettledUtc).Min()
                    ?? i.Adjustments.Where(a => a.Kind == AdjustmentKind.CreditNote)
                        .Select(a => (DateTimeOffset?)a.CreatedUtc).Max(),
            });
}

/// <summary>
/// D04 — the server side of the authoritative financial read path (<c>GET /finance/ledger</c>). Projects the
/// tenant's invoices/payments that changed at or after a timestamp cursor into the flat, display-shaped wire
/// rows the peer finance cache upserts by id. Global tenant filters apply to every set; strictly read-only.
/// </summary>
public sealed class FinanceLedgerReader(AnimoraServerDbContext db) : IFinanceLedgerReader
{
    /// <summary>Hard ceiling on one window so a never-polled peer can never ask for an unbounded result.</summary>
    private const int MaxWindow = 500;

    public async Task<FinanceReadPage> GetLedgerSinceAsync(long sinceTicks, int maxRows, CancellationToken ct)
    {
        var take = maxRows <= 0 ? 100 : Math.Min(maxRows, MaxWindow);
        var since = new DateTimeOffset(Math.Max(sinceTicks, 0), TimeSpan.Zero);

        // An invoice's effective "last change" is the newest of its lifecycle timestamps: it is re-delivered
        // when it is posted or settled/voided later, not only when it is first issued. The client upserts by
        // id, so re-delivery is idempotent. Draft invoices (never posted, no money) are excluded — they are
        // the owning device's private work until posted; a peer only needs posted/void financial state.
        // Phase 1 selects ONLY (id, effective tick) so the window filter/order is one simple translatable query.
        // It reads a full window (not just `take`) so a page can be cut on a timestamp BOUNDARY: every imported
        // invoice shares one UpdatedAt, so cutting inside that group made the cursor land on the group's own tick,
        // the next poll asked for `>= that tick` again and got the identical page — the peer re-applied the same
        // rows on every pass forever (and collided with whatever command was writing).
        var windowQuery =
            from i in db.Set<Invoice>().AsNoTracking()
            where i.State != InvoiceState.Draft
            let posted = i.PostedUtc ?? i.IssuedUtc
            let settled = i.SettledUtc ?? i.IssuedUtc
            let lifecycle = posted > settled ? posted : settled
            // UpdatedAt is transport bookkeeping, used only for delivery, never as a business event timestamp.
            let effective = i.UpdatedAt > lifecycle ? i.UpdatedAt : lifecycle
            where effective >= since
            orderby effective, i.Id
            select new { i.Id, Effective = effective };

        var wideWindow = await windowQuery.Take(MaxWindow + 1).ToListAsync(ct).ConfigureAwait(false);
        var window = TrimToTickBoundary(wideWindow, take, w => w.Effective.UtcTicks);
        var invoicesMore = wideWindow.Count > window.Count;

        var paymentQuery =
            from p in db.Set<Payment>().AsNoTracking()
            where p.ReceivedUtc >= since
            orderby p.ReceivedUtc, p.Id
            select new FinancePaymentRow(
                p.Id,
                p.InvoiceId,
                (int)p.Method,
                p.Amount.Minor,
                p.ReceivedUtc);

        var widePayments = await paymentQuery.Take(MaxWindow + 1).ToListAsync(ct).ConfigureAwait(false);
        var paymentRows = TrimToTickBoundary(widePayments, take, r => r.ReceivedUtc.UtcTicks);
        var paymentsMore = widePayments.Count > paymentRows.Count;

        // A payment changes its invoice's paid/due totals but not necessarily any lifecycle timestamp (a partial
        // payment leaves Posted/Settled untouched), so every invoice that received a payment in this window is
        // shipped alongside it — otherwise a peer would show the receipt against a stale balance.
        var inWindow = window.Select(w => w.Id).ToHashSet(StringComparer.Ordinal);
        var ids = window.Select(w => w.Id).ToList();
        ids.AddRange(paymentRows.Select(p => p.InvoiceId).Distinct(StringComparer.Ordinal).Where(id => !inWindow.Contains(id)));
        var invoices = await LoadInvoiceRowsAsync(ids, ct).ConfigureAwait(false);

        // Window rows keep their DB order (effective, id); the payment-driven extras follow, ordered by tick.
        var order = window.Select((w, index) => (w.Id, index)).ToDictionary(x => x.Id, x => x.index, StringComparer.Ordinal);
        invoices = invoices
            .OrderBy(r => order.TryGetValue(r.Id, out var index) ? index : int.MaxValue)
            .ThenBy(r => r.EffectiveTicks).ThenBy(r => r.Id, StringComparer.Ordinal)
            .ToList();

        // The next cursor must never jump past an unreturned row. When a collection was truncated, the cursor is
        // capped at the LAST row actually returned from the truncated collection(s) (the other collection simply
        // re-delivers rows above the cap on the next poll — upserts are idempotent). Only when nothing was
        // truncated is it safe to advance to the newest tick seen. An idle poll never rewinds the cursor.
        long next = sinceTicks;
        long? ceiling = null;
        if (invoicesMore && window.Count > 0) ceiling = window[^1].Effective.UtcTicks;
        if (paymentsMore && paymentRows.Count > 0)
        {
            var paymentCap = paymentRows[^1].ReceivedUtc.UtcTicks;
            ceiling = ceiling is { } c ? Math.Min(c, paymentCap) : paymentCap;
        }

        if (ceiling is { } cap) next = Math.Max(next, cap);
        else
        {
            // Nothing was truncated, so every row at the newest tick is already in this page: step PAST it.
            // Landing on it (the old behaviour) made the next poll re-deliver the same rows indefinitely.
            if (window.Count > 0) next = Math.Max(next, window[^1].Effective.UtcTicks + 1);
            if (paymentRows.Count > 0) next = Math.Max(next, paymentRows[^1].ReceivedUtc.UtcTicks + 1);
        }

        return new FinanceReadPage(invoices, paymentRows, next, invoicesMore || paymentsMore);
    }

    /// <summary>
    /// Trims an over-fetched window down to <paramref name="take"/> rows **without cutting a group of equal
    /// timestamps in half**. A page that ends inside such a group cannot move the cursor (the next poll asks for
    /// <c>&gt;=</c> the group's own tick and gets the same rows back), so the group is extended to its natural end
    /// when it still fits the hard window ceiling. Bulk imports stamp thousands of invoices with one timestamp, so
    /// this is the normal case there, not an edge case.
    /// </summary>
    private static List<T> TrimToTickBoundary<T>(List<T> rows, int take, Func<T, long> tick)
    {
        if (rows.Count <= take) return rows;
        var boundary = tick(rows[take - 1]);
        var end = take;
        while (end < rows.Count && tick(rows[end]) == boundary) end++;
        return end <= MaxWindow ? rows.GetRange(0, end) : rows.GetRange(0, take);
    }

    /// <summary>Phase 2: the display-shaped rows for a set of invoice ids (owner/pet names joined in).</summary>
    private async Task<List<FinanceInvoiceRow>> LoadInvoiceRowsAsync(List<string> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        var voidTimes = FinanceVoidProjection.Query(db);
        var rows = await (
            from i in db.Set<Invoice>().AsNoTracking()
            where ids.Contains(i.Id) && i.State != InvoiceState.Draft
            join owner in db.Set<Owner>().AsNoTracking() on i.OwnerId equals owner.Id into owners
            from owner in owners.DefaultIfEmpty()
            join pet in db.Set<Pet>().AsNoTracking() on i.PetId equals pet.Id into pets
            from pet in pets.DefaultIfEmpty()
            select new
            {
                i.Id,
                i.InvoiceNumber,
                Kind = (int)i.Kind,
                State = (int)i.State,
                i.OwnerId,
                PartyName = owner == null ? (i.GuestName ?? "") : owner.FullName,
                PetName = pet == null ? "" : pet.Name,
                i.IssuedUtc,
                i.PostedUtc,
                i.SettledUtc,
                i.UpdatedAt,
                i.GuestName, i.InsurerId, i.InsurancePercent, i.DiscountKind, i.DiscountValue,
                SubtotalMinor = i.Subtotal.Minor, DiscountMinor = i.DiscountTotal.Minor,
                TaxMinor = i.TaxTotal.Minor, InsuranceMinor = i.InsuranceCoverage.Minor,
                VoidedUtc = voidTimes.Where(v => v.InvoiceId == i.Id)
                    .Select(v => v.OccurredUtc).FirstOrDefault(),
                GrandTotalMinor = i.GrandTotal.Minor,
                PaidTotalMinor = i.PaidTotal.Minor,
                DueTotalMinor = i.DueTotal.Minor,
            }).ToListAsync(ct).ConfigureAwait(false);

        // Separate bounded child queries avoid a lines × adjustments Cartesian product.
        var lines = await db.Set<Invoice>().AsNoTracking().Where(i => ids.Contains(i.Id))
            .SelectMany(i => i.Lines.Select(l => new { InvoiceId = i.Id,
                Line = new FinanceInvoiceLine(l.LineId, (int)l.Kind, l.ProductId, l.VariantId,
                    l.CatalogItemId, l.Title, l.Quantity.Value, l.UnitPrice.Minor, l.DiscountPercent, l.TaxPercent) }))
            .ToListAsync(ct).ConfigureAwait(false);
        var adjustments = await db.Set<Invoice>().AsNoTracking().Where(i => ids.Contains(i.Id))
            .SelectMany(i => i.Adjustments.Select(a => new { InvoiceId = i.Id,
                Adjustment = new FinanceInvoiceAdjustment(a.AdjustmentId, (int)a.Kind, a.Amount.Minor, a.Reason, a.CreatedUtc) }))
            .ToListAsync(ct).ConfigureAwait(false);
        var lineLookup = lines.ToLookup(l => l.InvoiceId, l => l.Line);
        var adjustmentLookup = adjustments.ToLookup(a => a.InvoiceId, a => a.Adjustment);
        return rows.Select(r => new FinanceInvoiceRow(
            r.Id, r.InvoiceNumber, r.Kind, r.State, r.OwnerId, r.PartyName, r.PetName,
            r.IssuedUtc, r.PostedUtc, r.GrandTotalMinor, r.PaidTotalMinor, r.DueTotalMinor,
            EffectiveTicks(r.UpdatedAt > r.IssuedUtc ? r.UpdatedAt : r.IssuedUtc, r.PostedUtc, r.SettledUtc), r.VoidedUtc,
            new FinanceInvoiceDetail(r.GuestName, r.InsurerId, r.InsurancePercent, r.SubtotalMinor,
                r.DiscountMinor, r.TaxMinor, r.InsuranceMinor, (int)r.DiscountKind, r.DiscountValue,
                lineLookup[r.Id].ToArray(), adjustmentLookup[r.Id].ToArray()))).ToList();
    }

    /// <summary>Newest of the lifecycle timestamps — the in-memory twin of the phase-1 SQL <c>effective</c> expression.</summary>
    private static long EffectiveTicks(DateTimeOffset issued, DateTimeOffset? posted, DateTimeOffset? settled)
    {
        var latest = issued;
        if (posted is { } p && p > latest) latest = p;
        if (settled is { } s && s > latest) latest = s;
        return latest.UtcTicks;
    }
}
