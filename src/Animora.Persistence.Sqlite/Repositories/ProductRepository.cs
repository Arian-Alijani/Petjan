using Animora.Application.Ports;
using Animora.Domain.Inventory;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Repositories;

public sealed class ProductRepository(AnimoraLocalDbContext db) : IProductRepository
{
    public Task<Product?> FindAsync(string id, CancellationToken ct = default) =>
        db.Set<Product>().AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<Product> RequireAsync(string id, CancellationToken ct = default) =>
        await FindAsync(id, ct) ?? throw new InvalidOperationException($"Product {id} not found.");

    public void Add(Product entity) => db.Set<Product>().Add(entity);
    public void AddRange(IEnumerable<Product> entities) => db.Set<Product>().AddRange(entities);
}
