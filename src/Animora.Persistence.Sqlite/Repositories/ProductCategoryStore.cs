using Animora.Application.Ports;
using Animora.Domain.Inventory;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;
namespace Animora.Persistence.Sqlite.Repositories;

public sealed class ProductCategoryStore(AnimoraLocalDbContext db) : IProductCategoryStore
{
    public Task<ProductCategory?> FindAsync(string id, CancellationToken ct) =>
        db.Set<ProductCategory>().AsTracking().SingleOrDefaultAsync(c => c.Id == id, ct);
    public Task<bool> HasProductsAsync(string id, CancellationToken ct) =>
        db.Set<Product>().AnyAsync(p => p.CategoryId == id, ct);
    public Task<bool> HasChildrenAsync(string id, CancellationToken ct) =>
        db.Set<ProductCategory>().AnyAsync(c => c.ParentCategoryId == id, ct);
}
