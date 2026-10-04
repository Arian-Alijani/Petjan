using Animora.Application.Numbering;
using Animora.Application.Ports;
using Animora.Persistence.Sqlite.Commands;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Seed;

namespace Animora.Persistence.Sqlite.Finance;

/// <summary>
/// Allocates expense numbers atomically from <c>LocalSequenceCounters</c>, exactly like
/// <c>InvoiceNumberAllocator</c>: the counter participates in the ambient database transaction, so a
/// failed post cannot consume or expose a number. The sequence is tenant/Jalali-year scoped
/// (<c>expense:{tenantId}:{jalaliYear}</c>) and the number is formatted <c>EXP-{yy}-{seq:000000}</c>.
/// </summary>
public sealed class ExpenseNumberAllocator(
    AnimoraLocalDbContext db,
    ISeedPeriodCodeProvider periodCodes) : IExpenseNumberAllocator
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
            $"expense:{tenantId}:{jalaliYear}",
            ct).ConfigureAwait(false);

        return DocumentNumber.Format(DocumentNumber.ExpensePrefix, jalaliYear, sequence);
    }
}
