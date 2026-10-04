using Animora.Application.Ports;
using Animora.Domain.Inventory;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Repositories;

public sealed class ProductCategoryRepository(AnimoraLocalDbContext db) : IProductCategoryRepository
{
    public Task<ProductCategory?> FindAsync(string id, CancellationToken ct = default) =>
        db.Set<ProductCategory>().AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    public void Add(ProductCategory entity) => db.Set<ProductCategory>().Add(entity);

    public async Task<bool> NameExistsAsync(string name, string? excludeId, CancellationToken ct = default)
    {
        // Category lists are tiny (tens of rows): compare in memory so Persian text is matched exactly and
        // case-insensitively without depending on SQLite's ASCII-only NOCASE collation.
        var target = name.Trim();
        var names = await db.Set<ProductCategory>().AsNoTracking()
            .Where(c => excludeId == null || c.Id != excludeId)
            .Select(c => c.DisplayName)
            .ToListAsync(ct);
        for (var i = 0; i < names.Count; i++)
        {
            if (string.Equals(names[i].Trim(), target, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    public Task<int> CountProductsAsync(string categoryId, CancellationToken ct = default) =>
        db.Set<Product>().AsNoTracking().CountAsync(p => p.CategoryId == categoryId, ct);

    public async Task<int> NextSortOrderAsync(CancellationToken ct = default) =>
        (await db.Set<ProductCategory>().AsNoTracking().MaxAsync(c => (int?)c.SortOrder, ct) ?? -1) + 1;
}
