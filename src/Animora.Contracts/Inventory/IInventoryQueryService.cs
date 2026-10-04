namespace Animora.Contracts.Inventory;

/// <summary>
/// Data seam for the Inventory screen (product list + catalog settings + the
/// product / new-product / stock-adjust modals). The desktop ViewModel depends
/// only on this interface; the in-memory <c>MockInventoryQueryService</c> is the
/// single swappable implementation a future backend replaces (R-18).
/// </summary>
public interface IInventoryQueryService
{
    /// <summary>
    /// One page of the clinic's product catalog. Search, category / stock filters, ordering and paging all run
    /// server-side, so the screen never holds (or silently truncates) the whole catalog.
    /// </summary>
    Task<ProductPage> GetProductsAsync(ProductListQuery query);

    /// <summary>
    /// The product categories, mirroring the reference's <c>INV.CATS</c> — the
    /// filter chips, category tags and category-settings list all read this.
    /// </summary>
    Task<IReadOnlyList<CategoryInfo>> GetCategoriesAsync();

    /// <summary>
    /// One product plus its stock-movement ledger, mirroring what the reference's
    /// <c>openProductModal</c>/<c>renderProductModal</c> read from
    /// <c>INV.PRODUCTS</c> and <c>INV.movesOf(p.id)</c>.
    /// </summary>
    Task<ProductDetail?> GetDetailAsync(string productId);

    /// <summary>
    /// Persists the whole product modal draft — base info, the variant dimension
    /// and the variant rows — mirroring the reference's «ذخیره تغییرات».
    /// </summary>
    Task SaveProductInfoAsync(string productId, ProductInfoUpdate update);

    /// <summary>
    /// Appends one variant to a product, mirroring <c>makeVariant</c> invoked by
    /// the variants tab's «تنوع دستی» action.
    /// </summary>
    Task<ProductVariant> AddVariantAsync(string productId, NewVariantRequest request);

    /// <summary>The product's stock-movement ledger (newest first), for the History tab.</summary>
    Task<IReadOnlyList<StockMovement>> GetHistoryAsync(string productId);

    /// <summary>Creates a product from the new-product modal, mirroring <c>openNewProductModal</c>'s create handler.</summary>
    Task<Product> CreateProductAsync(NewProductRequest request);

    /// <summary>Removes a product, mirroring the product modal's «حذف کالا».</summary>
    Task DeleteProductAsync(string productId);

    /// <summary>
    /// Sets the on-hand stock of one or more of a product's variants in one atomic command and records a
    /// reasoned movement per changed variant, mirroring the stock-adjust modal's «ثبت موجودی».
    /// </summary>
    Task AdjustStockAsync(string productId, StockAdjustRequest request);
}
