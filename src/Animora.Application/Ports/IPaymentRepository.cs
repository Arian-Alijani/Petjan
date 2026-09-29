using Animora.Domain.Sales;

namespace Animora.Application.Ports;

public interface IPaymentRepository
{
    Task<Payment?> FindAsync(string id, CancellationToken ct = default);
    Task<Payment> RequireAsync(string id, CancellationToken ct = default);
    Task<Payment?> FindLatestForInvoiceAsync(string invoiceId, CancellationToken ct = default);
    void Add(Payment entity);
    void AddRange(IEnumerable<Payment> entities);
}
