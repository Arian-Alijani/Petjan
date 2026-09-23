using Animora.Domain.Purchasing;

namespace Animora.Application.Ports;

public interface IPurchasingRepository
{
    Task<PurchaseOrder?> FindAsync(string id, CancellationToken ct = default);
    Task<PurchaseOrder> RequireAsync(string id, CancellationToken ct = default);
    void Add(PurchaseOrder entity);
    void AddRange(IEnumerable<PurchaseOrder> entities);
}
