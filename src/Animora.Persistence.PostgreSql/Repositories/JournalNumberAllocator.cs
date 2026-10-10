using Animora.Application.Numbering;
using Animora.Application.Ports;
namespace Animora.Persistence.PostgreSql.Repositories;
public sealed class JournalNumberAllocator(DocumentCounterAllocator counters) : IJournalNumberAllocator
{
    public async Task<string> AllocateAsync(string tenantId, string periodCode, CancellationToken ct = default)
    {
        var year = DocumentNumber.JalaliYearOf(periodCode);
        var next = await counters.NextAsync(tenantId, "journal", year, ct);
        return DocumentNumber.Format(DocumentNumber.JournalPrefix, year, next);
    }
}
