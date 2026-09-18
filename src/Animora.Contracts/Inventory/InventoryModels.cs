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
