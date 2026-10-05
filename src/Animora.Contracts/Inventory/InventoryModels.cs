namespace Animora.Contracts.Inventory;

/// <summary>
/// A product category. <see cref="Tone"/> drives the categorical tag color, resolved by the View (this
/// contract stays free of UI resources). Categories carry no icon.
/// </summary>
public sealed record CategoryInfo(
    string Id,
    string Label,
    string Tone,
    int ProductCount = 0);

/// <summary>The list screen's stock-status filter chip (کم‌موجود / در دسترس), applied server-side.</summary>
public enum ProductStockFilter
{
    /// <summary>No stock filter.</summary>
    All,

    /// <summary>Products with at least one variant at/below its reorder point (low or out).</summary>
    Low,

    /// <summary>Products with no variant at/below its reorder point.</summary>
    Ok,
}

/// <summary>One server-side catalog page request: filters, search term and the page window.</summary>
/// <param name="Search">Name / code / variant-SKU search term, or null.</param>
/// <param name="CategoryId">Category filter, or null for all categories.</param>
/// <param name="Stock">Stock-status filter.</param>
/// <param name="Page">1-based page index.</param>
/// <param name="PageSize">Rows per page (clamped by the backend).</param>
public sealed record ProductListQuery(
    string? Search, string? CategoryId, ProductStockFilter Stock, int Page, int PageSize);

/// <summary>One catalog page plus the filtered total the pager needs.</summary>
/// <param name="Items">The page's products.</param>
/// <param name="TotalCount">Products matching the request's filters (pager).</param>
public sealed record ProductPage(IReadOnlyList<Product> Items, int TotalCount);

/// <summary>
/// A stock unit / label dimension a product's variants are offered in — the
/// reference's <c>product.attribute</c> (e.g. «دوز», «وزن», «حجم»). Null for a
/// single-unit product that carries no variant dimension.
/// </summary>
public sealed record UnitInfo(
    string Name,
    IReadOnlyList<string> Options);

/// <summary>
/// One purchasable variant of a product, mirroring the fields the reference's
/// <c>renderProductRow</c> / inline sub-row render from a
/// <c>PRODUCTS[].variants[]</c> entry: its own SKU, stock, reorder point, cost,
/// sale price and (Jalali) expiry. <see cref="Stock"/> is the source of truth;
/// product totals are derived from it (see <c>MockInventoryQueryService</c>).
/// </summary>
public sealed record ProductVariant(
    string Id,
    string Label,
    string Sku,
    int Stock,
    int ReorderPoint,
    long Cost,
    long Price,
    string? Expiry);

/// <summary>
/// A product in the inventory catalog, mirroring the shape the reference's
/// <c>renderProductRow</c> / <c>findVariant</c> read from <c>INV.PRODUCTS</c>:
/// its code, name/brand, category, one or more <see cref="Variants"/>, and the
/// optional variant <see cref="Attribute"/> (the «تنوع» dimension). Products
/// carry no icon/thumbnail. All pricing/stock figures are derived from the variants by the query
/// service — never stored twice.
/// </summary>
public sealed record Product(
    string Id,
    string Code,
    string Name,
    string Brand,
    string CategoryId,
    string Description,
    UnitInfo? Attribute,
    IReadOnlyList<ProductVariant> Variants);

/// <summary>
/// One stock-movement ledger entry, mirroring a row of the reference's
/// <c>INV.MOVES</c> (Animora-UI/assets/inventory-data.js). <see cref="Kind"/> is
/// <c>in</c> / <c>out</c> / <c>adjust</c>; <see cref="Qty"/> is signed for an
/// adjustment and positive for a purchase/sale. Dates/times are Jalali strings
/// as the reference carries them.
/// </summary>
public sealed record StockMovement(
    string Id,
    string ProductId,
    string? VariantId,
    string Kind,
    int Qty,
    string Source,
    string By,
    string Date,
    string Time,
    string? Note);

/// <summary>
/// A product plus its stock-movement ledger — the product modal's data need
/// (reference <c>openProductModal</c> reads <c>PRODUCTS.find</c> + <c>movesOf</c>).
/// </summary>
public sealed record ProductDetail(
    Product Product,
    IReadOnlyList<StockMovement> Movements);

/// <summary>
/// The product modal's full draft: base info, the optional variant dimension and
/// the variant rows — mirroring what the reference's «ذخیره تغییرات» writes back
/// onto the in-memory <c>PRODUCTS</c> entry.
/// </summary>
public sealed record ProductInfoUpdate(
    string Name,
    string Code,
    string Brand,
    string CategoryId,
    string Description,
    string? AttributeName,
    IReadOnlyList<ProductVariant> Variants);

/// <summary>One page of a product's stock-movement ledger plus the total number of movements.</summary>
/// <param name="Items">The page's movements, newest first.</param>
/// <param name="TotalCount">All movements the product has.</param>
public sealed record MovementPage(IReadOnlyList<StockMovement> Items, int TotalCount);

/// <summary>
/// A request to create a new product from the modal, mirroring the reference's
/// <c>openNewProductModal</c> create handler — single-unit, multi-variant or a stock-free service.
/// </summary>
/// <param name="IsService">Creates a stock-free service (<c>ProductKind.Service</c>) instead of a physical good.</param>
/// <param name="DefaultCost">Cost applied to every variant of a multi-variant product.</param>
/// <param name="DefaultPrice">Sale price applied to every variant of a multi-variant product (or the service rate).</param>
public sealed record NewProductRequest(
    string Code,
    string Name,
    string Brand,
    string CategoryId,
    string Description,
    string? AttributeName,
    IReadOnlyList<string> AttributeOptions,
    IReadOnlyList<ProductVariant> Variants,
    bool IsService = false,
    long DefaultCost = 0,
    long DefaultPrice = 0);

/// <summary>One variant's absolute target on-hand in a <see cref="StockAdjustRequest"/>.</summary>
public sealed record StockAdjustLine(string VariantId, int Stock);

/// <summary>
/// A request to set the on-hand stock of one or more of a product's variants, mirroring the stock-adjust
/// modal's «ثبت موجودی». <see cref="Reason"/> is the stable reason code recorded on every resulting movement
/// (<c>Count</c>, <c>Damage</c>, <c>Expiry</c>, <c>Theft</c>, <c>Correction</c>). The whole request is applied
/// atomically, with the delta computed against the freshly-read on-hand at write time.
/// </summary>
public sealed record StockAdjustRequest(
    IReadOnlyList<StockAdjustLine> Lines,
    string Reason = StockAdjustReasons.Correction,
    string? Note = null);

/// <summary>The reason codes a stock adjustment may carry (mirrors the domain's declared set).</summary>
public static class StockAdjustReasons
{
    public const string Count = "Count";
    public const string Damage = "Damage";
    public const string Expiry = "Expiry";
    public const string Theft = "Theft";
    public const string Correction = "Correction";
}
