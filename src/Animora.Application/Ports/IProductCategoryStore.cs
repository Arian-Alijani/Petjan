using Animora.Domain.Inventory;
namespace Animora.Application.Ports;

/// <summary>Tracked category aggregate and deletion blockers, scoped to the current tenant.</summary>
public interface IProductCategoryStore
{
    Task<ProductCategory?> FindAsync(string id, CancellationToken ct);
    Task<bool> HasProductsAsync(string id, CancellationToken ct);
    Task<bool> HasChildrenAsync(string id, CancellationToken ct);
}
