using Animora.Domain.Inventory;

namespace Animora.Application.Ports;

public interface IStockMovementRepository
{
    Task<StockMovement?> FindAsync(string id, CancellationToken ct = default);
    Task<StockMovement> RequireAsync(string id, CancellationToken ct = default);
    void Add(StockMovement entity);
    void AddRange(IEnumerable<StockMovement> entities);
}
