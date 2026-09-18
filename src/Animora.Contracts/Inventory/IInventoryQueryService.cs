namespace Animora.Contracts.Inventory;

/// <summary>
/// Data seam for the Inventory screen (product list + catalog settings). The
/// desktop ViewModel depends only on this interface; the in-memory
/// <c>MockInventoryQueryService</c> is the single swappable implementation a
/// future backend replaces (R-18).
/// </summary>
public interface IInventoryQueryService
{
    /// <summary>
    /// The clinic's product catalog, mirroring the reference's
    /// <c>INV.PRODUCTS</c> collection that the product table, sub-nav counts and
    /// category settings derive from.
    /// </summary>
    Task<IReadOnlyList<Product>> GetProductsAsync();

    /// <summary>
    /// The product categories, mirroring the reference's <c>INV.CATS</c> — the
    /// filter chips, category tags and category-settings list all read this.
    /// </summary>
    Task<IReadOnlyList<CategoryInfo>> GetCategoriesAsync();
}
