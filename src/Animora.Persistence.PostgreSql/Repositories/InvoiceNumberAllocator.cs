using Animora.Application.Numbering;
using Animora.Application.Ports;

namespace Animora.Persistence.PostgreSql.Repositories;

public sealed class InvoiceNumberAllocator(DocumentCounterAllocator counters) : IInvoiceNumberAllocator
{
    public async Task<string> AllocateAsync(string tenantId, DateTimeOffset issuedUtc, CancellationToken ct = default)
    {
        var year = DocumentCounterAllocator.Year(issuedUtc);
        var next = await counters.NextAsync(tenantId, "invoice", year, ct);
        return DocumentNumber.Format(DocumentNumber.InvoicePrefix, year, next);
    }
}
