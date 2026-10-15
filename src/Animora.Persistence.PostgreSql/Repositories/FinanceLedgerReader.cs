using Animora.Application.Ports;
using Animora.Contracts.Wire.Sync;
using Animora.Domain.Patients;
using Animora.Domain.Sales;
using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Repositories;

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
        var window = await (
            from i in db.Set<Invoice>().AsNoTracking()
            where i.State != InvoiceState.Draft
            let posted = i.PostedUtc ?? i.IssuedUtc
            let settled = i.SettledUtc ?? i.IssuedUtc
            let lifecycle = posted > settled ? posted : settled
            // UpdatedAt is transport bookkeeping, used only for delivery, never as a business event timestamp.
            let effective = i.UpdatedAt > lifecycle ? i.UpdatedAt : lifecycle
            where effective >= since
            orderby effective, i.Id
            select new { i.Id, Effective = effective })
            .Take(take + 1).ToListAsync(ct).ConfigureAwait(false);

        var paymentRows = await (
            from p in db.Set<Payment>().AsNoTracking()
            where p.ReceivedUtc >= since
            orderby p.ReceivedUtc, p.Id
            select new FinancePaymentRow(
                p.Id,
                p.InvoiceId,
                (int)p.Method,
                p.Amount.Minor,
                p.ReceivedUtc))
            .Take(take + 1).ToListAsync(ct).ConfigureAwait(false);

        // One extra row per collection signals "there is more past this window"; trim it back off before returning.
        var invoicesMore = window.Count > take;
        var paymentsMore = paymentRows.Count > take;
        if (invoicesMore) window.RemoveAt(window.Count - 1);
        if (paymentsMore) paymentRows.RemoveAt(paymentRows.Count - 1);

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
            if (window.Count > 0) next = Math.Max(next, window[^1].Effective.UtcTicks);
            if (paymentRows.Count > 0) next = Math.Max(next, paymentRows[^1].ReceivedUtc.UtcTicks);
        }

        return new FinanceReadPage(invoices, paymentRows, next, invoicesMore || paymentsMore);
    }

    /// <summary>Phase 2: the display-shaped rows for a set of invoice ids (owner/pet names joined in).</summary>
    private async Task<List<FinanceInvoiceRow>> LoadInvoiceRowsAsync(List<string> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
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
                VoidedUtc = i.State == InvoiceState.Void ? i.Adjustments.Where(a => a.Kind == AdjustmentKind.CreditNote)
                    .Select(a => (DateTimeOffset?)a.CreatedUtc).Max() : null,
                GrandTotalMinor = i.GrandTotal.Minor,
                PaidTotalMinor = i.PaidTotal.Minor,
                DueTotalMinor = i.DueTotal.Minor,
            }).ToListAsync(ct).ConfigureAwait(false);

        return rows.Select(r => new FinanceInvoiceRow(
            r.Id, r.InvoiceNumber, r.Kind, r.State, r.OwnerId, r.PartyName, r.PetName,
            r.IssuedUtc, r.PostedUtc, r.GrandTotalMinor, r.PaidTotalMinor, r.DueTotalMinor,
            EffectiveTicks(r.UpdatedAt > r.IssuedUtc ? r.UpdatedAt : r.IssuedUtc, r.PostedUtc, r.SettledUtc), r.VoidedUtc)).ToList();
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
