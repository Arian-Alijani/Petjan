using Animora.Domain.ValueObjects;

namespace Animora.Domain.Inventory;

/// <summary>
/// One purchasable/stockable variant of a product: its own SKU, prices, reorder point and optional
/// barcode.
/// </summary>
/// <remarks>
/// <para>
/// A stock-tracked product <b>always</b> has at least one variant; a product with no real variant
/// dimension carries a single variant with <see cref="AttributeName"/> = <see cref="DefaultAttributeName"/>.
/// </para>
/// <para>
/// Every <see cref="StockMovement"/> references a <see cref="VariantId"/> and never a bare product
/// id — that is what makes on-hand computation uniform across single- and multi-variant products
/// (see <see cref="StockCalculator"/>).
/// </para>
/// </remarks>
/// <param name="VariantId">ULID identifying the variant inside its product.</param>
/// <param name="Sku">Variant SKU, unique per tenant.</param>
/// <param name="AttributeName">The variant dimension, e.g. «وزن»; <see cref="DefaultAttributeName"/> for none.</param>
/// <param name="AttributeValue">The value on that dimension, e.g. <c>10KG</c>.</param>
/// <param name="CostPrice">Default purchase price per unit.</param>
/// <param name="SalePrice">Default sale price per unit.</param>
/// <param name="ReorderLevel">On-hand level at or below which the variant is flagged for reorder.</param>
/// <param name="Barcode">Scanner barcode; <see langword="null"/> when the variant has none.</param>
/// <param name="IsActive"><see langword="false"/> retires the variant without deleting its history.</param>
public sealed record ProductVariant(
    string VariantId,
    string Sku,
    string AttributeName,
    string AttributeValue,
    Money CostPrice,
    Money SalePrice,
    Quantity ReorderLevel,
    string? Barcode,
    bool IsActive)
{
    /// <summary>The attribute name of the implicit single variant of a product without variants.</summary>
    public const string DefaultAttributeName = "DEFAULT";

    /// <summary><see langword="true"/> when this is the implicit single variant.</summary>
    public bool IsDefault =>
        string.Equals(AttributeName, DefaultAttributeName, StringComparison.Ordinal);
}
