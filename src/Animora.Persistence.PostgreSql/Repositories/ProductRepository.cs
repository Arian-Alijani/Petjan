using Animora.Application.Ports;
using Animora.Domain.Inventory;
using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Repositories;

public sealed class ProductRepository(AnimoraServerDbContext db) : TenantRepository<Product>(db), IProductRepository
{
    public Task<Product?> FindAsync(string id, CancellationToken ct = default) =>
        Context.Set<Product>().Local.FirstOrDefault(e => e.Id == id && !e.Deleted) is { } local
            ? Task.FromResult<Product?>(local) : Context.Set<Product>().AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<Product> RequireAsync(string id, CancellationToken ct = default) =>
        await FindAsync(id, ct) ?? throw new InvalidOperationException($"Product {id} not found.");

    public new void Add(Product entity) => Context.Set<Product>().Add(entity);
    public void AddRange(IEnumerable<Product> entities) => Context.Set<Product>().AddRange(entities);
}
