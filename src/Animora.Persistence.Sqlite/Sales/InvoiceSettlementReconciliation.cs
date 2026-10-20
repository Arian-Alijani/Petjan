using Animora.Domain.Sales;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Sales;

/// <summary>
/// SYN-02: settlement-driven reconciliation of the local invoice back to the server-authoritative image.
/// Money is deliberately pull-excluded from the state feed, so the authoritative post-settlement figures arrive through the peer finance
/// ledger (<see cref="PeerInvoice"/>, keyed by the invoice id == creating operation id). Once the server has ACCEPTED the invoice's commands,
/// the local row stops being provisional: it adopts the server invoice number (the one thing the settlement contract cannot carry, and the
/// only figure the device allocates on its own). Totals/paid/due are never rewritten here — the ledger is append-only and reads of a settled
/// invoice are already served from the server image (<c>FinanceInvoiceProjection</c> substitutes the peer row once the invoice is settled).
/// <para>
/// Idempotent (re-running changes nothing), never touches a refused/conflicting/pending invoice, and runs inside the caller's transaction/save.
/// A server number that would collide with another local invoice's number (unique per tenant) is skipped and retried on the next pass.
/// </para>
/// </summary>
public static class InvoiceSettlementReconciliation
{
    /// <summary>Adopts the server number for one settled local invoice when its peer twin is cached. Returns true when the number changed.</summary>
    public static async Task<bool> AdoptAsync(AnimoraLocalDbContext db, Invoice invoice, CancellationToken ct)
    {
        var serverNumber = await db.Set<PeerInvoice>().AsNoTracking()
            .Where(p => p.TenantId == invoice.TenantId && p.Id == invoice.Id)
            .Select(p => p.Number).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return serverNumber is { Length: > 0 } && await TryAdoptAsync(db, invoice, serverNumber, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Sweeps every settled local invoice whose cached peer twin carries a different number. Returns the invoices updated (the caller saves).
    /// </summary>
    public static async Task<int> SweepAsync(AnimoraLocalDbContext db, string tenantId, CancellationToken ct)
    {
        var drifted = await (from i in db.Set<Invoice>().AsTracking()
                             join p in db.Set<PeerInvoice>() on i.Id equals p.Id
                             where p.TenantId == tenantId && i.State != InvoiceState.Draft
                                   && i.Settlement == SettlementStatus.Settled && i.InvoiceNumber != p.Number
                             select new { Invoice = i, ServerNumber = p.Number }).ToListAsync(ct).ConfigureAwait(false);
        var changed = 0;
        for (var i = 0; i < drifted.Count; i++)
            if (await TryAdoptAsync(db, drifted[i].Invoice, drifted[i].ServerNumber, ct).ConfigureAwait(false)) changed++;
        return changed;
    }

    private static async Task<bool> TryAdoptAsync(AnimoraLocalDbContext db, Invoice invoice, string serverNumber, CancellationToken ct)
    {
        var number = serverNumber.Trim();
        if (string.Equals(invoice.InvoiceNumber, number, StringComparison.Ordinal)) return false;
        // Uniqueness is per tenant (IX_Invoices_TenantId_Number): never let an adoption violate it - retry on a later pass instead.
        var taken = await db.Set<Invoice>().AsNoTracking()
            .AnyAsync(x => x.TenantId == invoice.TenantId && x.InvoiceNumber == number && x.Id != invoice.Id, ct).ConfigureAwait(false);
        if (taken) return false;
        var adopted = invoice.AdoptServerInvoiceNumber(number);
        return adopted.IsSuccess && adopted.Value;
    }
}
