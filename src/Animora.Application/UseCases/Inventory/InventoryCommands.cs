using Animora.Application.Abstractions;
using Animora.Application.Auditing;
using Animora.Application.Commands;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.Validation;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Identity;
using Animora.Domain.Inventory;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Domain.ValueObjects;

namespace Animora.Application.UseCases.Inventory;

/// <summary>
/// One variant as supplied by a caller: prices in minor units, the reorder level at the store's exact
/// ×1000 quantity scale (see <c>FixedScaleQuantityConverter</c>). <paramref name="VariantId"/> is
/// <see langword="null"/> to append a new variant and set to replace an existing one.
/// </summary>
public sealed record ProductVariantInput(
    string? VariantId,
    string Sku,
    string AttributeName,
    string AttributeValue,
    long CostMinor,
    long SaleMinor,
    long ReorderLevelScaled,
    string? Barcode = null);

/// <summary>Creates a product together with its variants. Returns the new product id.</summary>
[RequiresPermission(PermissionCodes.InventoryAdjust)]
[Auditable(AuditCategory.Inventory, CommandTypes.InventoryProductCreate)]
public sealed record CreateProductCommand(
    string Sku,
    string Name,
    string CategoryId,
    string UnitCode,
    ProductKind Kind,
    long DefaultCostMinor,
    long DefaultSaleMinor,
    IReadOnlyList<ProductVariantInput> Variants,
    string? Brand = null,
    bool IsPrescriptionOnly = false,
    long ReorderLevelScaled = 0,
    string? Notes = null) : ICommand<string>, IJournaledCommand
{
    /// <inheritdoc />
    public string CommandTypeName => CommandTypes.InventoryProductCreate;

    /// <inheritdoc />
    public string AggregateTypeName => "Product";

    /// <inheritdoc />
    public string? AggregateIdOrNull => null;
}

public sealed class CreateProductCommandValidator : IValidator<CreateProductCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(CreateProductCommand c, CancellationToken ct)
    {
        var failures = new List<ValidationFailure>();
        if (Rules.Required(c.Sku, nameof(c.Sku)) is { } sku) failures.Add(sku);
        if (Rules.Required(c.Name, nameof(c.Name)) is { } name) failures.Add(name);
        if (Rules.Required(c.CategoryId, nameof(c.CategoryId)) is { } category) failures.Add(category);
        if (Rules.Required(c.UnitCode, nameof(c.UnitCode)) is { } unit) failures.Add(unit);
        if (Rules.ValidEnum(c.Kind, nameof(c.Kind)) is { } kind) failures.Add(kind);
        if (Rules.MaxLength(c.Sku, 64, nameof(c.Sku)) is { } skuLength) failures.Add(skuLength);
        if (Rules.MaxLength(c.Name, 256, nameof(c.Name)) is { } nameLength) failures.Add(nameLength);
        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

public sealed class CreateProductCommandHandler(IProductRepository products, IClock clock)
    : ICommandHandler<CreateProductCommand, string>
{
    public async Task<Result<string>> HandleAsync(CreateProductCommand c, CommandContext context, CancellationToken ct)
    {
        if (c.Kind == ProductKind.Service)
            return Result<string>.Fail(ErrorCodes.ServiceNoStock,
                "A service is created as a catalogue item without stock tracking; use the catalogue editor.");

        var unit = c.UnitCode.Trim();
        var variantInputs = c.Variants;
        if (variantInputs.Count == 0)
            return Result<string>.Fail(ErrorCodes.VariantRequired, "A stock-tracked product needs at least one variant.");

        // Variant SKU uniqueness inside the product is the domain's own invariant (PRODUCT_SKU_DUPLICATE),
        // enforced by Product.Create as it appends each variant (A06).
        var variants = new List<ProductVariant>(variantInputs.Count);
        for (var i = 0; i < variantInputs.Count; i++)
        {
            variants.Add(NewVariant(variantInputs[i], unit, clock.UtcNow));
        }

        var created = Product.Create(
            context.TenantId, c.Sku, c.Name, c.CategoryId, c.Kind, unit,
            Money.Irt(c.DefaultCostMinor), Money.Irt(c.DefaultSaleMinor), variants,
            trackStock: true, isPrescriptionOnly: c.IsPrescriptionOnly,
            reorderLevel: QuantityOf(c.ReorderLevelScaled, unit), brand: c.Brand, notes: c.Notes);
        if (created.IsFailure) return Result<string>.Fail(created.Error, created.Detail);

        var product = created.Value!;
        products.Add(product);
        return Result<string>.Ok(product.Id);
    }

    private static ProductVariant NewVariant(ProductVariantInput input, string unit, DateTimeOffset now) =>
        new(IdGenerator.NewId(now), input.Sku.Trim(), input.AttributeName.Trim(), input.AttributeValue.Trim(),
            Money.Irt(input.CostMinor), Money.Irt(input.SaleMinor),
            QuantityOf(input.ReorderLevelScaled, unit), input.Barcode, true);

    private static Quantity QuantityOf(long scaled, string unit) => new(scaled / 1000m, unit);
}

/// <summary>Replaces a product's descriptive fields, prices, reorder level and variant set.</summary>
[RequiresPermission(PermissionCodes.InventoryAdjust)]
[Auditable(AuditCategory.Inventory, CommandTypes.InventoryProductUpdate)]
public sealed record UpdateProductInfoCommand(
    string ProductId,
    string Name,
    long DefaultCostMinor,
    long DefaultSaleMinor,
    IReadOnlyList<ProductVariantInput> Variants,
    string? Brand = null,
    string? Notes = null,
    long? ReorderLevelScaled = null) : ICommand<Unit>, IJournaledCommand
{
    /// <inheritdoc />
    public string CommandTypeName => CommandTypes.InventoryProductUpdate;

    /// <inheritdoc />
    public string AggregateTypeName => "Product";

    /// <inheritdoc />
    public string? AggregateIdOrNull => ProductId;
}

public sealed class UpdateProductInfoCommandValidator : IValidator<UpdateProductInfoCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(UpdateProductInfoCommand c, CancellationToken ct)
    {
        var failures = new List<ValidationFailure>();
        if (Rules.Required(c.ProductId, nameof(c.ProductId)) is { } product) failures.Add(product);
        if (Rules.Required(c.Name, nameof(c.Name)) is { } name) failures.Add(name);
        if (Rules.MaxLength(c.Name, 256, nameof(c.Name)) is { } length) failures.Add(length);
        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

public sealed class UpdateProductInfoCommandHandler(IProductRepository products, IClock clock)
    : ICommandHandler<UpdateProductInfoCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(UpdateProductInfoCommand c, CommandContext context, CancellationToken ct)
    {
        var product = await products.FindAsync(c.ProductId, ct);
        if (product is null) return Result<Unit>.Fail(ErrorCodes.ProductNotFound);

        var described = product.Describe(c.Name, c.Brand, c.Notes);
        if (described.IsFailure) return Result<Unit>.Fail(described.Error, described.Detail);

        var priced = product.SetPrices(Money.Irt(c.DefaultCostMinor), Money.Irt(c.DefaultSaleMinor));
        if (priced.IsFailure) return Result<Unit>.Fail(priced.Error, priced.Detail);

        if (c.ReorderLevelScaled is { } level)
        {
            var reordered = product.SetReorderLevel(new Quantity(level / 1000m, product.UnitCode));
            if (reordered.IsFailure) return Result<Unit>.Fail(reordered.Error, reordered.Detail);
        }

        return ApplyVariants(product, c.Variants, clock.UtcNow);
    }

    private static Result<Unit> ApplyVariants(
        Product product, IReadOnlyList<ProductVariantInput> inputs, DateTimeOffset now)
    {
        for (var i = 0; i < inputs.Count; i++)
        {
            var input = inputs[i];
            if (input.VariantId is { Length: > 0 } id && FindVariant(product, id) is { } existing)
            {
                var updated = product.UpdateVariant(existing with
                {
                    Sku = input.Sku.Trim(),
                    AttributeName = input.AttributeName.Trim(),
                    AttributeValue = input.AttributeValue.Trim(),
                    CostPrice = Money.Irt(input.CostMinor),
                    SalePrice = Money.Irt(input.SaleMinor),
                    ReorderLevel = new Quantity(input.ReorderLevelScaled / 1000m, product.UnitCode),
                    Barcode = input.Barcode,
                });
                if (updated.IsFailure) return Result<Unit>.Fail(updated.Error, updated.Detail);
                continue;
            }

            var added = product.AddVariant(new ProductVariant(
                IdGenerator.NewId(now), input.Sku.Trim(), input.AttributeName.Trim(), input.AttributeValue.Trim(),
                Money.Irt(input.CostMinor), Money.Irt(input.SaleMinor),
                new Quantity(input.ReorderLevelScaled / 1000m, product.UnitCode), input.Barcode, true));
            if (added.IsFailure) return Result<Unit>.Fail(added.Error, added.Detail);
        }

        return Result<Unit>.Ok(Unit.Value);
    }

    private static ProductVariant? FindVariant(Product product, string variantId)
    {
        for (var i = 0; i < product.Variants.Count; i++)
        {
            if (string.Equals(product.Variants[i].VariantId, variantId, StringComparison.Ordinal))
            {
                return product.Variants[i];
            }
        }

        return null;
    }
}

/// <summary>Appends one manually-authored variant to a product. Returns the new variant id.</summary>
[RequiresPermission(PermissionCodes.InventoryAdjust)]
[Auditable(AuditCategory.Inventory, CommandTypes.InventoryVariantAdd)]
public sealed record AddProductVariantCommand(
    string ProductId,
    string Sku,
    string AttributeName,
    string AttributeValue,
    long CostMinor,
    long SaleMinor,
    long ReorderLevelScaled = 0,
    string? Barcode = null) : ICommand<string>, IJournaledCommand
{
    /// <inheritdoc />
    public string CommandTypeName => CommandTypes.InventoryVariantAdd;

    /// <inheritdoc />
    public string AggregateTypeName => "Product";

    /// <inheritdoc />
    public string? AggregateIdOrNull => ProductId;
}

public sealed class AddProductVariantCommandValidator : IValidator<AddProductVariantCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(AddProductVariantCommand c, CancellationToken ct)
    {
        var failures = new List<ValidationFailure>();
        if (Rules.Required(c.ProductId, nameof(c.ProductId)) is { } product) failures.Add(product);
        if (Rules.Required(c.Sku, nameof(c.Sku)) is { } sku) failures.Add(sku);
        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

public sealed class AddProductVariantCommandHandler(IProductRepository products, IClock clock)
    : ICommandHandler<AddProductVariantCommand, string>
{
    public async Task<Result<string>> HandleAsync(AddProductVariantCommand c, CommandContext context, CancellationToken ct)
    {
        var product = await products.FindAsync(c.ProductId, ct);
        if (product is null) return Result<string>.Fail(ErrorCodes.ProductNotFound);

        var variantId = IdGenerator.NewId(clock.UtcNow);
        var added = product.AddVariant(new ProductVariant(
            variantId, c.Sku.Trim(), c.AttributeName.Trim(), c.AttributeValue.Trim(),
            Money.Irt(c.CostMinor), Money.Irt(c.SaleMinor),
            new Quantity(c.ReorderLevelScaled / 1000m, product.UnitCode), c.Barcode, true));
        return added.IsFailure
            ? Result<string>.Fail(added.Error, added.Detail)
            : Result<string>.Ok(variantId);
    }
}

/// <summary>
/// Soft-deletes a product: it is deactivated and tombstoned, never physically removed. Refused with
/// <c>PRODUCT_HAS_STOCK</c> while the product still has on-hand stock or a movement in the last 90 days.
/// </summary>
[RequiresPermission(PermissionCodes.InventoryAdjust)]
[Auditable(AuditCategory.Inventory, CommandTypes.InventoryProductDelete)]
public sealed record DeleteProductCommand(string ProductId) : ICommand<Unit>, IJournaledCommand
{
    /// <summary>The window in which recent movement blocks a delete.</summary>
    public const int RecentMovementDays = 90;

    /// <inheritdoc />
    public string CommandTypeName => CommandTypes.InventoryProductDelete;

    /// <inheritdoc />
    public string AggregateTypeName => "Product";

    /// <inheritdoc />
    public string? AggregateIdOrNull => ProductId;
}

public sealed class DeleteProductCommandValidator : IValidator<DeleteProductCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(DeleteProductCommand c, CancellationToken ct)
    {
        var failures = new List<ValidationFailure>();
        if (Rules.Required(c.ProductId, nameof(c.ProductId)) is { } product) failures.Add(product);
        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

public sealed class DeleteProductCommandHandler(IProductRepository products, IInventoryQueries queries, IClock clock)
    : ICommandHandler<DeleteProductCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(DeleteProductCommand c, CommandContext context, CancellationToken ct)
    {
        var product = await products.FindAsync(c.ProductId, ct);
        if (product is null) return Result<Unit>.Fail(ErrorCodes.ProductNotFound);

        if (await queries.GetProductOnHandScaledAsync(product.Id, ct) != 0L)
            return Result<Unit>.Fail(ErrorCodes.ProductHasStock,
                $"Product '{product.Id}' still has on-hand stock and cannot be deleted.");

        var since = clock.UtcNow.AddDays(-DeleteProductCommand.RecentMovementDays);
        if (await queries.CountMovementsSinceAsync(product.Id, since, ct) > 0)
            return Result<Unit>.Fail(ErrorCodes.ProductHasStock,
                $"Product '{product.Id}' moved stock in the last {DeleteProductCommand.RecentMovementDays} days.");

        var deactivated = product.Deactivate();
        if (deactivated.IsFailure) return Result<Unit>.Fail(deactivated.Error, deactivated.Detail);
        product.Deleted = true;
        return Result<Unit>.Ok(Unit.Value);
    }
}

/// <summary>
/// Records a manual stock correction: one signed <see cref="StockMovement"/> whose kind follows the sign
/// of the delta. <paramref name="DeltaQuantity"/> is the delta at the store's ×1000 quantity scale.
/// </summary>
[RequiresPermission(PermissionCodes.InventoryAdjust)]
[Auditable(AuditCategory.Inventory, CommandTypes.InventoryStockAdjust)]
public sealed record AdjustStockCommand(
    string ProductId,
    string VariantId,
    long DeltaQuantity,
    string ReasonCode,
    string? Note = null) : ICommand<Unit>, IJournaledCommand
{
    /// <inheritdoc />
    public string CommandTypeName => CommandTypes.InventoryStockAdjust;

    /// <inheritdoc />
    public string AggregateTypeName => "Product";

    /// <inheritdoc />
    public string? AggregateIdOrNull => ProductId;
}

public sealed class AdjustStockCommandValidator : IValidator<AdjustStockCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(AdjustStockCommand c, CancellationToken ct)
    {
        var failures = new List<ValidationFailure>();
        if (Rules.Required(c.ProductId, nameof(c.ProductId)) is { } product) failures.Add(product);
        if (Rules.Required(c.VariantId, nameof(c.VariantId)) is { } variant) failures.Add(variant);
        if (Rules.Required(c.ReasonCode, nameof(c.ReasonCode)) is { } reason) failures.Add(reason);
        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

public sealed class AdjustStockCommandHandler(
    IProductRepository products, IStockMovementRepository movements, IInventoryQueries queries, IClock clock)
    : ICommandHandler<AdjustStockCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(AdjustStockCommand c, CommandContext context, CancellationToken ct)
    {
        if (!AdjustmentReasons.IsValid(c.ReasonCode))
            return Result<Unit>.Fail(ErrorCodes.AdjustmentReasonInvalid,
                $"'{c.ReasonCode}' is not a declared adjustment reason.");

        var product = await products.FindAsync(c.ProductId, ct);
        if (product is null) return Result<Unit>.Fail(ErrorCodes.ProductNotFound);

        if (!HasVariant(product, c.VariantId))
            return Result<Unit>.Fail(ErrorCodes.VariantNotFound,
                $"Variant '{c.VariantId}' is not on product '{product.Id}'.");

        // Nothing to record: a zero delta is never a movement (the factory forbids a zero quantity).
        if (c.DeltaQuantity == 0L)
            return Result<Unit>.Ok(Unit.Value);

        var current = await queries.GetOnHandScaledAsync(product.Id, c.VariantId, ct);
        var projected = current + c.DeltaQuantity;
        if (projected < 0L)
            return Result<Unit>.Fail(ErrorCodes.StockWouldGoNegative,
                $"On-hand {current / 1000m} plus delta {c.DeltaQuantity / 1000m} would go negative.");

        var now = clock.UtcNow;
        var quantity = new Quantity(c.DeltaQuantity / 1000m, product.UnitCode);
        var kind = c.DeltaQuantity > 0L ? MovementKind.AdjustmentIn : MovementKind.AdjustmentOut;
        var created = StockMovement.Create(
            context.TenantId, product.Id, c.VariantId, kind, quantity, Money.Irt(0), now, context.UserId,
            operationId: context.OperationId, reason: c.ReasonCode);
        if (created.IsFailure) return Result<Unit>.Fail(created.Error, created.Detail);

        movements.Add(created.Value!);
        return Result<Unit>.Ok(Unit.Value);
    }

    private static bool HasVariant(Product product, string variantId)
    {
        for (var i = 0; i < product.Variants.Count; i++)
        {
            if (string.Equals(product.Variants[i].VariantId, variantId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
