using Animora.Application.Numbering;
using Animora.Application.Ports;
using Animora.Domain.Sales;
using Animora.Persistence.Sqlite.Commands;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Seed;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Sales;

/// <summary>
/// Allocates invoice numbers atomically from <c>LocalSequenceCounters</c>. The counter participates in
/// the ambient database transaction, so a failed posting cannot consume or expose a number. Format:
/// <c>INV-YY-NNNNNN</c> (see <see cref="DocumentNumber"/>); the counter key is unchanged so numbering continues.
/// </summary>
public sealed class InvoiceNumberAllocator(
    AnimoraLocalDbContext db,
    ISeedPeriodCodeProvider periodCodes) : IInvoiceNumberAllocator
{
    public async Task<string> AllocateAsync(
        string tenantId,
        DateTimeOffset issuedUtc,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        // F-07: an invoice number must only ever be consumed inside an ambient UnitOfWork transaction, so a
        // failed posting rolls the counter back and never leaves a gap in the invoice sequence. Without one,
        // NextSequenceAsync runs on an autonomous connection and consumes the number immediately — a silent
        // gap risk if the caller later fails. Make the contract explicit (dev/CI) rather than relying on it.
        System.Diagnostics.Debug.Assert(db.Database.CurrentTransaction is not null,
            "InvoiceNumberAllocator must run inside an ambient UnitOfWork transaction to avoid invoice-number gaps.");

        var jalaliYear = DocumentNumber.JalaliYearOf(periodCodes.For(issuedUtc).PeriodCode);

        // The counter can legitimately sit BEHIND the numbers already in use: SYN-02 adoption replaces a
        // provisional number with the server's, and an import/restore can bring numbers the local counter never
        // issued. Handing out one of those again violates IX_Invoices_TenantId_Number, which surfaces as a
        // confusing CONCURRENCY_CONFLICT on every post. So skip anything already taken — the counter moves inside
        // the ambient transaction, so the skipped values are consumed atomically and no gap is left behind.
        for (var attempt = 0; attempt < MaxCollisionSkips; attempt++)
        {
            var sequence = await PipelineSql.NextSequenceAsync(
                db,
                $"invoice:{tenantId}:{jalaliYear}",
                ct).ConfigureAwait(false);

            var candidate = DocumentNumber.Format(DocumentNumber.InvoicePrefix, jalaliYear, sequence);
            var taken = await db.Set<Invoice>().AsNoTracking()
                .AnyAsync(i => i.TenantId == tenantId && i.InvoiceNumber == candidate, ct).ConfigureAwait(false);
            if (!taken) return candidate;
        }

        throw new InvalidOperationException(
            $"Could not allocate a free invoice number for tenant '{tenantId}' after {MaxCollisionSkips} attempts.");
    }

    /// <summary>Upper bound on skipping already-used numbers before giving up (a corrupted counter must not spin).</summary>
    private const int MaxCollisionSkips = 500;
}
