using Animora.Application.Ports;
using Animora.Domain.Inventory;
using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Repositories;

public sealed class ProductCategoryRepository(AnimoraServerDbContext db) : TenantRepository<ProductCategory>(db), IProductCategoryRepository
{
    public Task<ProductCategory?> FindAsync(string id, CancellationToken ct = default) =>
        db.Set<ProductCategory>().Local.FirstOrDefault(e => e.Id == id && !e.Deleted) is { } local
            ? Task.FromResult<ProductCategory?>(local) : db.Set<ProductCategory>().AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    public new void Add(ProductCategory entity) => db.Set<ProductCategory>().Add(entity);

    public async Task<bool> NameExistsAsync(string name, string? excludeId, CancellationToken ct = default)
    {
        var target = name.Trim().ToUpperInvariant();
        return await db.Set<ProductCategory>().AsNoTracking().AnyAsync(c =>
            (excludeId == null || c.Id != excludeId) && c.DisplayName.Trim().ToUpper() == target, ct);
    }

    public Task<int> CountProductsAsync(string categoryId, CancellationToken ct = default) =>
        db.Set<Product>().AsNoTracking().CountAsync(p => p.CategoryId == categoryId, ct);

    public async Task<int> NextSortOrderAsync(CancellationToken ct = default) =>
        (await db.Set<ProductCategory>().AsNoTracking().MaxAsync(c => (int?)c.SortOrder, ct) ?? -1) + 1;
}
