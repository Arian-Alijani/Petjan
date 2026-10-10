using Animora.Domain.Inventory;

namespace Animora.Persistence.PostgreSql.Repositories;

/// <summary>Append-only stock write seam. A sale must never discard a movement.</summary>
public interface IStockMovementWriter
{
    void Append(StockMovement movement);
}
