using Animora.Application.Numbering;
using Animora.Application.Ports;
using Animora.Domain.Finance;
using Animora.Persistence.Sqlite.Commands;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Finance;

/// <summary>
/// Atomically advances the same transactional sequence table used for invoice numbers. Journal numbers are
/// Jalali-year scoped (<c>journal:{tenantId}:{yyyy}</c>) and formatted <c>JV-YY-NNNNNN</c>; the posting period
/// still travels separately on the entry, so the month is not encoded in the number.
/// </summary>
public sealed class JournalNumberAllocator(AnimoraLocalDbContext db) : IJournalNumberAllocator
{
    /// <summary>Upper bound on skipping already-used numbers before giving up (a corrupted counter must not spin).</summary>
    private const int MaxCollisionSkips = 500;

    public async Task<string> AllocateAsync(string tenantId, string periodCode, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(periodCode);
        var jalaliYear = DocumentNumber.JalaliYearOf(periodCode);

        // Same guard as the invoice allocator: a restore/import can leave the counter behind numbers that already
        // exist, and handing one of those out again violates the unique (TenantId, EntryNumber) index — which
        // surfaces as a misleading CONCURRENCY_CONFLICT on an otherwise valid post. Skip them; the counter moves
        // inside the ambient transaction, so the skipped values are consumed atomically and no gap is left.
        for (var attempt = 0; attempt < MaxCollisionSkips; attempt++)
        {
            var sequence = await PipelineSql.NextSequenceAsync(db, $"journal:{tenantId}:{jalaliYear}", ct)
                .ConfigureAwait(false);
            var candidate = DocumentNumber.Format(DocumentNumber.JournalPrefix, jalaliYear, sequence);
            var taken = await db.Set<JournalEntry>().AsNoTracking()
                .AnyAsync(e => e.TenantId == tenantId && e.EntryNumber == candidate, ct).ConfigureAwait(false);
            if (!taken) return candidate;
        }

        throw new InvalidOperationException(
            $"Could not allocate a free journal number for tenant '{tenantId}' after {MaxCollisionSkips} attempts.");
    }
}
