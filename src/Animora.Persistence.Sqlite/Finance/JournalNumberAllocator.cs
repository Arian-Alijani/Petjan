using Animora.Application.Numbering;
using Animora.Application.Ports;
using Animora.Persistence.Sqlite.Commands;
using Animora.Persistence.Sqlite.Context;

namespace Animora.Persistence.Sqlite.Finance;

/// <summary>
/// Atomically advances the same transactional sequence table used for invoice numbers. Journal numbers are
/// Jalali-year scoped (<c>journal:{tenantId}:{yyyy}</c>) and formatted <c>JV-YY-NNNNNN</c>; the posting period
/// still travels separately on the entry, so the month is not encoded in the number.
/// </summary>
public sealed class JournalNumberAllocator(AnimoraLocalDbContext db) : IJournalNumberAllocator
{
    public async Task<string> AllocateAsync(string tenantId, string periodCode, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(periodCode);
        var jalaliYear = DocumentNumber.JalaliYearOf(periodCode);
        var sequence = await PipelineSql.NextSequenceAsync(db, $"journal:{tenantId}:{jalaliYear}", ct)
            .ConfigureAwait(false);
        return DocumentNumber.Format(DocumentNumber.JournalPrefix, jalaliYear, sequence);
    }
}
