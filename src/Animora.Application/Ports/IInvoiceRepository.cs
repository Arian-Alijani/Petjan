using Animora.Domain.Sales;

namespace Animora.Application.Ports;

public interface IInvoiceRepository
{
    Task<Invoice?> FindAsync(string id, CancellationToken ct = default);
    Task<Invoice> RequireAsync(string id, CancellationToken ct = default);
    void Add(Invoice entity);
    void AddRange(IEnumerable<Invoice> entities);
}
