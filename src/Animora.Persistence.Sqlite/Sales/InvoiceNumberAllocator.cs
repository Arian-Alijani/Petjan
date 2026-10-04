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

        var jalaliYear = DocumentNumber.JalaliYearOf(periodCodes.For(issuedUtc).PeriodCode);

        var sequence = await PipelineSql.NextSequenceAsync(
            db,
            $"invoice:{tenantId}:{jalaliYear}",
            ct).ConfigureAwait(false);

        return DocumentNumber.Format(DocumentNumber.InvoicePrefix, jalaliYear, sequence);
    }
}
