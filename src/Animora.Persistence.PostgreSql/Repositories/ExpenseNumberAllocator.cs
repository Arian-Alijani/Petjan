using Animora.Application.Numbering;
using Animora.Application.Ports;
namespace Animora.Persistence.PostgreSql.Repositories;
public sealed class ExpenseNumberAllocator(DocumentCounterAllocator counters) : IExpenseNumberAllocator
{
    public async Task<string> AllocateAsync(string tenantId, DateTimeOffset issuedUtc, CancellationToken ct = default)
    {
        var year = DocumentCounterAllocator.Year(issuedUtc);
        var next = await counters.NextAsync(tenantId, "expense", year, ct);
        return DocumentNumber.Format(DocumentNumber.ExpensePrefix, year, next);
    }
}
