namespace Animora.Application.UseCases.Inventory.ReadModels;

/// <summary>
/// One catalog variant with its snapshot-derived on-hand. <see cref="OnHandScaled"/> comes from the
/// maintained <c>StockOnHandSnapshot</c> (C06) — never from a stored absolute on the domain (R-B23).
/// Its unit scale is ×1000 (<c>FixedScaleQuantityConverter</c>); the mapping divides by 1000.
/// </summary>
public sealed record ProductVariantReadModel(
    string VariantId,
    string Sku,
    string AttributeName,
    string AttributeValue,
    long CostMinor,
    long PriceMinor,
    long ReorderLevelScaled,
    string? Barcode,
    bool IsActive,
    long OnHandScaled);

/// <summary>One catalog product row for the inventory grid, with its variants and their on-hand.</summary>
public sealed record ProductReadModel(
    string Id,
    string Sku,
    string Name,
    string Brand,
    string CategoryId,
    string Kind,
    string UnitCode,
    string? Notes,
    bool IsActive,
    IReadOnlyList<ProductVariantReadModel> Variants);

/// <summary>A product category for the filter chips / settings list.</summary>
public sealed record CategoryReadModel(string Id, string Name, string ParentId);

/// <summary>One stock-movement ledger entry for the product history tab.</summary>
public sealed record MovementReadModel(
    string Id,
    string ProductId,
    string VariantId,
    string Kind,
    long QuantityScaled,
    long UnitCostMinor,
    DateTimeOffset OccurredUtc,
    string RecordedByUserId,
    string? SourceEntityType,
    string? SourceEntityId,
    string? Reason);

/// <summary>A product plus its last-100 movements — the product-modal data need.</summary>
public sealed record ProductDetailReadModel(
    ProductReadModel Product,
    IReadOnlyList<MovementReadModel> Movements);
