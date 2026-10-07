using Animora.Application.Numbering;
using Animora.Application.Ports;
using Animora.Persistence.Sqlite.Commands;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Seed;

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

        var sequence = await PipelineSql.NextSequenceAsync(
            db,
            $"invoice:{tenantId}:{jalaliYear}",
            ct).ConfigureAwait(false);

        return DocumentNumber.Format(DocumentNumber.InvoicePrefix, jalaliYear, sequence);
    }
}
