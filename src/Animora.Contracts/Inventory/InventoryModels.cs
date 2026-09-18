namespace Animora.Contracts.Inventory;

/// <summary>
/// A product category, mirroring a row of the reference's <c>INV.CATS</c>
/// (Animora-UI/assets/inventory-data.js). <see cref="Tone"/> drives the
/// categorical chip/tag color and <see cref="IconKey"/> the category glyph, both
/// resolved by the View (this contract stays free of UI resources).
/// </summary>
public sealed record CategoryInfo(
    string Id,
    string Label,
    string Tone,
    string IconKey);

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
/// its code, name/brand, category, thumbnail glyph + tone, one or more
/// <see cref="Variants"/>, and the optional variant <see cref="Attribute"/>
/// (the «تنوع» dimension). <see cref="Glyph"/> is the product thumbnail's
/// emoji character exactly as the reference carries it (<c>p.icon</c>).
/// All pricing/stock figures are derived from the variants by the query
/// service — never stored twice.
/// </summary>
public sealed record Product(
    string Id,
    string Code,
    string Name,
    string Brand,
    string CategoryId,
    string Glyph,
    string Tone,
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
    string Glyph,
    string Description,
    string? AttributeName,
    IReadOnlyList<ProductVariant> Variants);

/// <summary>
/// A request to append one manually-authored variant (reference
/// <c>makeVariant(p, 'تنوع جدید', …)</c> behind the variants tab's «تنوع دستی»).
/// </summary>
public sealed record NewVariantRequest(string Label);

/// <summary>
/// A request to create a new product from the modal, mirroring the reference's
/// <c>openNewProductModal</c> create handler — single-unit or multi-variant.
/// </summary>
public sealed record NewProductRequest(
    string Code,
    string Name,
    string Brand,
    string CategoryId,
    string Glyph,
    string Description,
    string? AttributeName,
    IReadOnlyList<string> AttributeOptions,
    IReadOnlyList<ProductVariant> Variants);

/// <summary>
/// A request to set one variant's on-hand stock, mirroring the stock-adjust
/// modal's «ثبت موجودی» (the delta is logged as a history event).
/// </summary>
public sealed record StockAdjustRequest(int Stock);
