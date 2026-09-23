using Animora.Domain.Inventory;

namespace Animora.Application.Ports;

public interface IProductRepository
{
    Task<Product?> FindAsync(string id, CancellationToken ct = default);
    Task<Product> RequireAsync(string id, CancellationToken ct = default);
    void Add(Product entity);
    void AddRange(IEnumerable<Product> entities);
}
