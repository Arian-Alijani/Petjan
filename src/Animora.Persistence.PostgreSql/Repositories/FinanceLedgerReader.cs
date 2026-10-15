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
        // `effective` is built with COALESCE-friendly null-coalescing so EF Core emits a single translatable
        // scalar (GREATEST over IssuedUtc, PostedUtc, SettledUtc) rather than a client-evaluated expression.
        var invoiceRows = await (
            from i in db.Set<Invoice>().AsNoTracking()
            where i.State != InvoiceState.Draft
            let posted = i.PostedUtc ?? i.IssuedUtc
            let settled = i.SettledUtc ?? i.IssuedUtc
            let effective = posted > settled
                ? (posted > i.IssuedUtc ? posted : i.IssuedUtc)
                : (settled > i.IssuedUtc ? settled : i.IssuedUtc)
            where effective >= since
            join owner in db.Set<Owner>().AsNoTracking() on i.OwnerId equals owner.Id into owners
            from owner in owners.DefaultIfEmpty()
            join pet in db.Set<Pet>().AsNoTracking() on i.PetId equals pet.Id into pets
            from pet in pets.DefaultIfEmpty()
            orderby effective, i.Id
            // `.UtcTicks` is not DB-translatable, so `effective` is selected as a DateTimeOffset here and the tick
            // is computed in memory (below). Everything else is a plain translatable column/scalar.
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
                GrandTotalMinor = i.GrandTotal.Minor,
                PaidTotalMinor = i.PaidTotal.Minor,
                DueTotalMinor = i.DueTotal.Minor,
                Effective = effective,
            })
            .Take(take + 1).ToListAsync(ct).ConfigureAwait(false);
        var invoices = invoiceRows.Select(r => new FinanceInvoiceRow(
            r.Id, r.InvoiceNumber, r.Kind, r.State, r.OwnerId, r.PartyName, r.PetName,
            r.IssuedUtc, r.PostedUtc, r.GrandTotalMinor, r.PaidTotalMinor, r.DueTotalMinor,
            r.Effective.UtcTicks)).ToList();

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
        var invoicesMore = invoices.Count > take;
        var paymentsMore = paymentRows.Count > take;
        if (invoicesMore) invoices.RemoveAt(invoices.Count - 1);
        if (paymentsMore) paymentRows.RemoveAt(paymentRows.Count - 1);

        // The next cursor is the max effective tick we actually returned (ascending order ⇒ the last row), so a
        // follow-up poll resumes exactly after it (advance to the newest tick seen, or keep the request cursor
        // when nothing changed so an idle peer never rewinds).
        long next = sinceTicks;
        if (invoices.Count > 0) next = Math.Max(next, invoices[^1].EffectiveTicks);
        if (paymentRows.Count > 0) next = Math.Max(next, paymentRows[^1].ReceivedUtc.UtcTicks);

        return new FinanceReadPage(invoices, paymentRows, next, invoicesMore || paymentsMore);
    }
}
