using Animora.Domain.Inventory;

namespace Animora.Application.Ports;

/// <summary>Write-side access to product categories for the category-management commands.</summary>
public interface IProductCategoryRepository
{
    Task<ProductCategory?> FindAsync(string id, CancellationToken ct = default);

    void Add(ProductCategory entity);

    /// <summary>Whether another live category (not <paramref name="excludeId"/>) already uses <paramref name="name"/> (case-insensitive).</summary>
    Task<bool> NameExistsAsync(string name, string? excludeId, CancellationToken ct = default);

    /// <summary>How many live products reference the category.</summary>
    Task<int> CountProductsAsync(string categoryId, CancellationToken ct = default);

    /// <summary>The next sibling ordering hint (max + 1) so a new category lands at the end of the list.</summary>
    Task<int> NextSortOrderAsync(CancellationToken ct = default);
}
