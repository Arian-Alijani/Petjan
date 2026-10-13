using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.ValueObjects;

namespace Animora.Domain.Inventory;

/// <summary>What kind of catalogue item a product is.</summary>
public enum ProductKind
{
    /// <summary>A medicine dispensed or administered to a patient.</summary>
    Medicine = 0,

    /// <summary>A vaccine.</summary>
    Vaccine = 1,

    /// <summary>Pet food.</summary>
    Food = 2,

    /// <summary>A pet-shop retail article.</summary>
    PetShop = 3,

    /// <summary>A clinic consumable (syringes, gloves, …).</summary>
    Consumable = 4,

    /// <summary>A durable piece of equipment.</summary>
    Equipment = 5,

    /// <summary>A service — labour, not goods. Never stock-tracked.</summary>
    Service = 6,
}

/// <summary>
/// The product aggregate root: one catalogue item with one or more <see cref="ProductVariant"/>s.
/// </summary>
/// <remarks>
/// <para>
/// <b>Stock is never stored here (R-B23, ARCHITECTURE.md §11.2).</b> This aggregate carries no
/// quantity-in-stock property of any kind: the current stock of a variant is <i>always</i> derived
/// from its <see cref="StockMovement"/> ledger by <see cref="StockCalculator"/>, and
/// <see cref="StockLevel"/> is only a rebuildable projection of that ledger. Adding a stored
/// absolute quantity to this type is a review-blocking defect.
/// </para>
/// <para>
/// Invariants: a <see cref="ProductKind.Service"/> is never stock-tracked
/// (<c>SERVICE_NO_STOCK</c>); a stock-tracked product always keeps at least one active variant
/// (<c>VARIANT_REQUIRED</c>); every variant's reorder level is expressed in the product's own
/// <see cref="UnitCode"/> (<c>UNIT_MISMATCH</c>); variant SKUs are unique inside the product
/// (<c>PRODUCT_SKU_DUPLICATE</c>). Every mutator returns <see cref="Result"/>.
/// </para>
/// </remarks>
public sealed class Product : AggregateRoot
{
    private readonly List<ProductVariant> _variants = [];

    /// <summary>EF Core materialization constructor. Not for business code.</summary>
    private Product()
    {
    }

    /// <summary>Product SKU, unique per tenant (<c>PRODUCT_SKU_DUPLICATE</c>, checked by A07/B-track).</summary>
    public string Sku { get; private set; } = string.Empty;

    /// <summary>Product name as it appears in pickers and on invoices.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Manufacturer or brand; <see langword="null"/> when unbranded.</summary>
    public string? Brand { get; private set; }

    /// <summary>The <see cref="ProductCategory"/> this product belongs to.</summary>
    public string CategoryId { get; private set; } = string.Empty;

    /// <summary>What kind of catalogue item this is.</summary>
    public ProductKind Kind { get; private set; }

    /// <summary>The unit every quantity of this product is expressed in (<see cref="UnitOfMeasure.Code"/>).</summary>
    public string UnitCode { get; private set; } = string.Empty;

    /// <summary><see langword="true"/> when the item may only be dispensed against a prescription.</summary>
    public bool IsPrescriptionOnly { get; private set; }

    /// <summary><see langword="false"/> retires the product from pickers without deleting its history.</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// <see langword="true"/> when the product participates in the movement ledger. Always
    /// <see langword="false"/> for <see cref="ProductKind.Service"/>.
    /// </summary>
    public bool TrackStock { get; private set; }

    /// <summary>Product-level reorder level; a variant may override it with its own.</summary>
    public Quantity ReorderLevel { get; private set; }

    /// <summary>Default purchase price per unit, used to seed purchase-order lines.</summary>
    public Money DefaultCostPrice { get; private set; }

    /// <summary>Default sale price per unit, used to seed invoice lines.</summary>
    public Money DefaultSalePrice { get; private set; }

    /// <summary>Shelf life in days, used to compute an expiry at receipt; <see langword="null"/> when non-perishable.</summary>
    public int? ShelfLifeDays { get; private set; }

    /// <summary>Free-form notes.</summary>
    public string? Notes { get; private set; }

    /// <summary>The product's variants; a stock-tracked product always has at least one active.</summary>
    public IReadOnlyList<ProductVariant> Variants => _variants;

    /// <summary>
    /// Creates a product. A stock-tracked product must be created with at least one variant
    /// (<c>VARIANT_REQUIRED</c>); a service must not be stock-tracked (<c>SERVICE_NO_STOCK</c>).
    /// </summary>
    public static Result<Product> Create(
        string tenantId,
        string sku,
        string name,
        string categoryId,
        ProductKind kind,
        string unitCode,
        Money defaultCostPrice,
        Money defaultSalePrice,
        IReadOnlyList<ProductVariant> variants,
        bool trackStock = true,
        bool isPrescriptionOnly = false,
        Quantity? reorderLevel = null,
        string? brand = null,
        int? shelfLifeDays = null,
        string? notes = null,
        string? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryId);
        ArgumentException.ThrowIfNullOrWhiteSpace(unitCode);
        ArgumentNullException.ThrowIfNull(variants);

        var unit = unitCode.Trim();

        if (kind == ProductKind.Service && trackStock)
        {
            return Result<Product>.Fail(
                ErrorCodes.ServiceNoStock,
                $"Product '{sku}' is a service and cannot be stock-tracked.");
        }

        if (trackStock && variants.Count == 0)
        {
            return Result<Product>.Fail(
                ErrorCodes.VariantRequired,
                $"Stock-tracked product '{sku}' needs at least one variant.");
        }

        if (shelfLifeDays is <= 0)
        {
            return Result<Product>.Fail(
                ErrorCodes.ValidationFailed,
                $"ShelfLifeDays {shelfLifeDays} must be positive when set.");
        }

        var level = reorderLevel ?? Quantity.Zero(unit);
        if (!string.Equals(level.UnitCode, unit, StringComparison.Ordinal))
        {
            return Result<Product>.Fail(
                ErrorCodes.UnitMismatch,
                $"Reorder level unit '{level.UnitCode}' does not match product unit '{unit}'.");
        }

        var product = new Product
        {
            Id = string.IsNullOrWhiteSpace(id) ? IdGenerator.NewId() : id,
            TenantId = tenantId,
            Sku = sku.Trim(),
            Name = name.Trim(),
            Brand = Clean(brand),
            CategoryId = categoryId,
            Kind = kind,
            UnitCode = unit,
            IsPrescriptionOnly = isPrescriptionOnly,
            IsActive = true,
            TrackStock = trackStock,
            ReorderLevel = level,
            DefaultCostPrice = defaultCostPrice,
            DefaultSalePrice = defaultSalePrice,
            ShelfLifeDays = shelfLifeDays,
            Notes = Clean(notes),
        };

        for (var i = 0; i < variants.Count; i++)
        {
            var added = product.AddVariant(variants[i]);
            if (added.IsFailure)
            {
                return Result<Product>.Fail(added.Error, added.Detail);
            }
        }

        return product;
    }

    /// <summary>
    /// Appends a variant. Its reorder level must use the product's unit (<c>UNIT_MISMATCH</c>) and
    /// its SKU must be unique inside the product (<c>PRODUCT_SKU_DUPLICATE</c>).
    /// </summary>
    public Result AddVariant(ProductVariant variant)
    {
        ArgumentNullException.ThrowIfNull(variant);

        var check = EnsureVariantValid(variant);
        if (check.IsFailure)
        {
            return check;
        }

        _variants.Add(variant);
        return Result.Ok();
    }

    /// <summary>Replaces an existing variant, identified by <see cref="ProductVariant.VariantId"/>.</summary>
    public Result UpdateVariant(ProductVariant variant)
    {
        ArgumentNullException.ThrowIfNull(variant);

        var index = IndexOf(variant.VariantId);
        if (index < 0)
        {
            return Result.Fail(ErrorCodes.NotFound, $"Variant '{variant.VariantId}' is not on product '{Id}'.");
        }

        var check = EnsureVariantValid(variant, ignoreIndex: index);
        if (check.IsFailure)
        {
            return check;
        }

        _variants[index] = variant;
        return Result.Ok();
    }

    /// <summary>
    /// Retires a variant. The last active variant of a stock-tracked product cannot be retired —
    /// its movements would have nothing to reference (<c>VARIANT_REQUIRED</c>).
    /// </summary>
    public Result DeactivateVariant(string variantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(variantId);

        var index = IndexOf(variantId);
        if (index < 0)
        {
            return Result.Fail(ErrorCodes.NotFound, $"Variant '{variantId}' is not on product '{Id}'.");
        }

        if (TrackStock && CountActiveExcept(index) == 0)
        {
            return Result.Fail(
                ErrorCodes.VariantRequired,
                $"Product '{Id}' is stock-tracked and must keep at least one active variant.");
        }

        _variants[index] = _variants[index] with { IsActive = false };
        return Result.Ok();
    }

    /// <summary>Sets the default cost and sale prices. Both must share the same currency.</summary>
    public Result SetPrices(Money costPrice, Money salePrice)
    {
        if (!string.Equals(costPrice.Currency, salePrice.Currency, StringComparison.Ordinal))
        {
            return Result.Fail(
                ErrorCodes.ValidationFailed,
                $"Cost currency '{costPrice.Currency}' and sale currency '{salePrice.Currency}' differ.");
        }

        DefaultCostPrice = costPrice;
        DefaultSalePrice = salePrice;
        return Result.Ok();
    }

    /// <summary>Sets the product-level reorder level; it must use the product's unit.</summary>
    public Result SetReorderLevel(Quantity reorderLevel)
    {
        if (!string.Equals(reorderLevel.UnitCode, UnitCode, StringComparison.Ordinal))
        {
            return Result.Fail(
                ErrorCodes.UnitMismatch,
                $"Reorder level unit '{reorderLevel.UnitCode}' does not match product unit '{UnitCode}'.");
        }

        if (reorderLevel.IsNegative)
        {
            return Result.Fail(
                ErrorCodes.ValidationFailed,
                $"Reorder level {reorderLevel.Value} cannot be negative.");
        }

        ReorderLevel = reorderLevel;
        return Result.Ok();
    }

    /// <summary>Updates the descriptive fields.</summary>
    public Result Describe(string name, string? brand, string? notes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name.Trim();
        Brand = Clean(brand);
        Notes = Clean(notes);
        return Result.Ok();
    }

    /// <summary>Moves the product to another category.</summary>
    public Result Reclassify(string categoryId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryId);

        CategoryId = categoryId.Trim();
        return Result.Ok();
    }

    /// <summary>
    /// Changes the product code (SKU). Tenant-wide uniqueness is enforced by the caller against the catalogue
    /// (<c>PRODUCT_SKU_DUPLICATE</c>) and, as a last line of defence, by the unique store index.
    /// </summary>
    public Result ChangeSku(string sku)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);

        Sku = sku.Trim();
        return Result.Ok();
    }

    /// <summary>Retires the product from pickers; its ledger and history stay intact.</summary>
    public Result Deactivate()
    {
        IsActive = false;
        return Result.Ok();
    }

    /// <summary>Returns the product to the pickers.</summary>
    public Result Activate()
    {
        IsActive = true;
        return Result.Ok();
    }

    private Result EnsureVariantValid(ProductVariant variant, int ignoreIndex = -1)
    {
        if (!string.Equals(variant.ReorderLevel.UnitCode, UnitCode, StringComparison.Ordinal))
        {
            return Result.Fail(
                ErrorCodes.UnitMismatch,
                $"Variant '{variant.VariantId}' uses unit '{variant.ReorderLevel.UnitCode}'; product unit is '{UnitCode}'.");
        }

        for (var i = 0; i < _variants.Count; i++)
        {
            if (i == ignoreIndex)
            {
                continue;
            }

            if (string.Equals(_variants[i].Sku, variant.Sku, StringComparison.Ordinal))
            {
                return Result.Fail(
                    ErrorCodes.ProductSkuDuplicate,
                    $"SKU '{variant.Sku}' is already used by another variant of product '{Id}'.");
            }

            if (string.Equals(_variants[i].VariantId, variant.VariantId, StringComparison.Ordinal))
            {
                return Result.Fail(
                    ErrorCodes.ValidationFailed,
                    $"Variant '{variant.VariantId}' already exists on product '{Id}'.");
            }
        }

        return Result.Ok();
    }

    private int IndexOf(string variantId)
    {
        for (var i = 0; i < _variants.Count; i++)
        {
            if (string.Equals(_variants[i].VariantId, variantId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private int CountActiveExcept(int index)
    {
        var count = 0;
        for (var i = 0; i < _variants.Count; i++)
        {
            if (i != index && _variants[i].IsActive)
            {
                count++;
            }
        }

        return count;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
