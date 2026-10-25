using Animora.Application.Numbering;
using Animora.Application.Ports;
using Animora.Domain.Finance;
using Animora.Persistence.Sqlite.Commands;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Seed;
using Microsoft.EntityFrameworkCore;

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
    /// <summary>Upper bound on skipping already-used numbers before giving up (a corrupted counter must not spin).</summary>
    private const int MaxCollisionSkips = 500;

    public async Task<string> AllocateAsync(
        string tenantId,
        DateTimeOffset issuedUtc,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var jalaliYear = DocumentNumber.JalaliYearOf(periodCodes.For(issuedUtc).PeriodCode);

        // Same guard as the invoice/journal allocators: never hand out a number that a restore or import already
        // put in the table — the unique index would reject it and the failure would read as a concurrency conflict.
        for (var attempt = 0; attempt < MaxCollisionSkips; attempt++)
        {
            var sequence = await PipelineSql.NextSequenceAsync(
                db,
                $"expense:{tenantId}:{jalaliYear}",
                ct).ConfigureAwait(false);

            var candidate = DocumentNumber.Format(DocumentNumber.ExpensePrefix, jalaliYear, sequence);
            var taken = await db.Set<Expense>().AsNoTracking()
                .AnyAsync(e => e.TenantId == tenantId && e.ExpenseNumber == candidate, ct).ConfigureAwait(false);
            if (!taken) return candidate;
        }

        throw new InvalidOperationException(
            $"Could not allocate a free expense number for tenant '{tenantId}' after {MaxCollisionSkips} attempts.");
    }
}
