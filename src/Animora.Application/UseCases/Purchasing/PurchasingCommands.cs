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
using Animora.Domain.Purchasing;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Domain.ValueObjects;

namespace Animora.Application.UseCases.Purchasing;

/// <summary>One ordered line, quantities at the store's exact ×1000 quantity scale.</summary>
public sealed record PurchaseOrderLineInput(
    string ProductId,
    string VariantId,
    long OrderedQuantityScaled,
    long UnitCostMinor,
    decimal DiscountPercent = 0m,
    decimal TaxPercent = 0m,
    string? LotNumber = null,
    DateTimeOffset? ExpiryUtc = null);

/// <summary>One received line; <paramref name="QuantityScaled"/> is the positive amount that arrived.</summary>
public sealed record GoodsReceiptLineInput(
    string ProductId,
    string VariantId,
    long QuantityScaled,
    long UnitCostMinor,
    string? LotNumber = null,
    DateTimeOffset? ExpiryUtc = null);

/// <summary>
/// Raises a purchase order for a supplier. Created as a <see cref="PurchaseOrderState.Draft"/> unless
/// <paramref name="Submit"/> is set, in which case its lines are frozen and it is sent immediately.
/// Returns the new order id.
/// </summary>
[RequiresPermission(PermissionCodes.PurchasingManage)]
[Auditable(AuditCategory.Inventory, CommandTypes.PurchasingOrderCreate)]
public sealed record CreatePurchaseOrderCommand(
    string SupplierId,
    IReadOnlyList<PurchaseOrderLineInput> Lines,
    DateTimeOffset? ExpectedUtc = null,
    string? Notes = null,
    bool Submit = false) : ICommand<string>, IJournaledCommand
{
    /// <inheritdoc />
    public string CommandTypeName => CommandTypes.PurchasingOrderCreate;

    /// <inheritdoc />
    public string AggregateTypeName => "PurchaseOrder";

    /// <inheritdoc />
    public string? AggregateIdOrNull => null;
}

public sealed class CreatePurchaseOrderCommandValidator : IValidator<CreatePurchaseOrderCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(CreatePurchaseOrderCommand c, CancellationToken ct)
    {
        var failures = new List<ValidationFailure>();
        if (Rules.Required(c.SupplierId, nameof(c.SupplierId)) is { } supplier) failures.Add(supplier);
        for (var i = 0; i < c.Lines.Count; i++)
        {
            var line = c.Lines[i];
            if (Rules.Required(line.ProductId, $"Lines[{i}].ProductId") is { } product) failures.Add(product);
            if (Rules.Required(line.VariantId, $"Lines[{i}].VariantId") is { } variant) failures.Add(variant);
            if (line.OrderedQuantityScaled <= 0L)
                failures.Add(new ValidationFailure($"Lines[{i}].OrderedQuantityScaled", Rules.RequiredCode));
        }
        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

public sealed class CreatePurchaseOrderCommandHandler(
    IPurchasingRepository purchasing, IProductRepository products, IPurchasingQueries queries, IClock clock)
    : ICommandHandler<CreatePurchaseOrderCommand, string>
{
    public async Task<Result<string>> HandleAsync(CreatePurchaseOrderCommand c, CommandContext context, CancellationToken ct)
    {
        if (c.Lines.Count == 0)
            return Result<string>.Fail(ErrorCodes.ValidationFailed, "A purchase order needs at least one line.");

        var now = clock.UtcNow;
        var sequence = await queries.CountOrdersAsync(ct) + 1;
        var created = PurchaseOrder.Create(
            context.TenantId, $"PO-{sequence:D5}", c.SupplierId, now, context.UserId,
            expectedUtc: c.ExpectedUtc, notes: c.Notes,
            id: IdGenerator.ForOperation(context.OperationId, 0, now));
        if (created.IsFailure) return Result<string>.Fail(created.Error, created.Detail);

        var order = created.Value!;
        for (var i = 0; i < c.Lines.Count; i++)
        {
            var input = c.Lines[i];
            var product = await products.FindAsync(input.ProductId, ct);
            if (product is null) return Result<string>.Fail(ErrorCodes.ProductNotFound);
            if (!HasVariant(product, input.VariantId))
                return Result<string>.Fail(ErrorCodes.VariantNotFound,
                    $"Variant '{input.VariantId}' is not on product '{product.Id}'.");

            var unit = product.UnitCode;
            var line = new PurchaseOrderLine(
                IdGenerator.ForOperation(context.OperationId, 1 + i, now), product.Id, input.VariantId,
                new Quantity(input.OrderedQuantityScaled / 1000m, unit),
                Quantity.Zero(unit),
                Money.Irt(input.UnitCostMinor),
                input.DiscountPercent, input.TaxPercent, input.LotNumber, input.ExpiryUtc);
            var added = order.AddLine(line);
            if (added.IsFailure) return Result<string>.Fail(added.Error, added.Detail);
        }

        if (c.Submit)
        {
            var submitted = order.Submit(clock);
            if (submitted.IsFailure) return Result<string>.Fail(submitted.Error, submitted.Detail);
        }

        purchasing.Add(order);
        return Result<string>.Ok(order.Id);
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

/// <summary>
/// Records a goods receipt and writes the stock ledger: one <see cref="MovementKind.Receipt"/> movement
/// per received line, carrying its unit cost. When <paramref name="PurchaseOrderId"/> is set the order's
/// received quantities advance and its state moves to PartiallyReceived / Received. The on-hand snapshot
/// is maintained by the store's save path (C06 T03), so the receipt and its movements land atomically.
/// </summary>
[RequiresPermission(PermissionCodes.InventoryReceive)]
[Auditable(AuditCategory.Inventory, CommandTypes.PurchasingReceiptRecord)]
public sealed record ReceiveGoodsCommand(
    string SupplierId,
    IReadOnlyList<GoodsReceiptLineInput> Lines,
    string? PurchaseOrderId = null,
    string? InvoiceReference = null,
    bool AllowOverReceipt = false) : ICommand<string>, IJournaledCommand
{
    /// <inheritdoc />
    public string CommandTypeName => CommandTypes.PurchasingReceiptRecord;

    /// <inheritdoc />
    public string AggregateTypeName => "GoodsReceipt";

    /// <inheritdoc />
    public string? AggregateIdOrNull => null;
}

public sealed class ReceiveGoodsCommandValidator : IValidator<ReceiveGoodsCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(ReceiveGoodsCommand c, CancellationToken ct)
    {
        var failures = new List<ValidationFailure>();
        if (Rules.Required(c.SupplierId, nameof(c.SupplierId)) is { } supplier) failures.Add(supplier);
        if (c.Lines.Count == 0)
            failures.Add(new ValidationFailure(nameof(c.Lines), Rules.RequiredCode));
        for (var i = 0; i < c.Lines.Count; i++)
        {
            var line = c.Lines[i];
            if (Rules.Required(line.ProductId, $"Lines[{i}].ProductId") is { } product) failures.Add(product);
            if (Rules.Required(line.VariantId, $"Lines[{i}].VariantId") is { } variant) failures.Add(variant);
            if (line.QuantityScaled <= 0L)
                failures.Add(new ValidationFailure($"Lines[{i}].QuantityScaled", Rules.RequiredCode));
        }
        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

public sealed class ReceiveGoodsCommandHandler(
    IPurchasingRepository purchasing, IProductRepository products,
    IStockMovementRepository movements, IPurchasingQueries queries, IClock clock)
    : ICommandHandler<ReceiveGoodsCommand, string>
{
    /// <summary>First deterministic-id slot for stock movements (receipt = 0, lines = 1…n) — see <see cref="IdGenerator.ForOperation"/>.</summary>
    private const int MovementSlotBase = 1_000_000;

    public async Task<Result<string>> HandleAsync(ReceiveGoodsCommand c, CommandContext context, CancellationToken ct)
    {
        PurchaseOrder? order = null;
        if (c.PurchaseOrderId is { Length: > 0 } orderId)
        {
            order = await purchasing.FindAsync(orderId, ct);
            if (order is null) return Result<string>.Fail(ErrorCodes.PurchaseOrderNotFound);
            if (order.State is not (PurchaseOrderState.Submitted or PurchaseOrderState.PartiallyReceived))
                return Result<string>.Fail(ErrorCodes.PurchaseOrderStateInvalid,
                    $"Order '{order.Id}' is {order.State}; goods can only be received while it is open.");
        }

        var now = clock.UtcNow;
        var receiptNumber = $"GR-{await queries.CountReceiptsAsync(ct) + 1:D5}";
        var createdReceipt = GoodsReceipt.Create(
            context.TenantId, receiptNumber, c.SupplierId, now, context.UserId,
            purchaseOrderId: order?.Id, invoiceReference: c.InvoiceReference,
            id: IdGenerator.ForOperation(context.OperationId, 0, now));
        if (createdReceipt.IsFailure) return Result<string>.Fail(createdReceipt.Error, createdReceipt.Detail);

        var receipt = createdReceipt.Value!;
        var stockMovements = new List<StockMovement>(c.Lines.Count);
        for (var i = 0; i < c.Lines.Count; i++)
        {
            var input = c.Lines[i];
            var product = await products.FindAsync(input.ProductId, ct);
            if (product is null) return Result<string>.Fail(ErrorCodes.ProductNotFound);

            var unit = product.UnitCode;
            var quantity = new Quantity(input.QuantityScaled / 1000m, unit);
            var unitCost = Money.Irt(input.UnitCostMinor);

            if (order is not null)
            {
                var over = EnsureWithinOrder(order, product.Id, input.VariantId, quantity, c.AllowOverReceipt);
                if (over.IsFailure) return Result<string>.Fail(over.Error, over.Detail);
            }

            var line = new GoodsReceiptLine(IdGenerator.ForOperation(context.OperationId, 1 + i, now), product.Id, input.VariantId,
                quantity, unitCost, input.LotNumber, input.ExpiryUtc);
            var addedLine = receipt.AddLine(line);
            if (addedLine.IsFailure) return Result<string>.Fail(addedLine.Error, addedLine.Detail);

            // The unit cost actually paid becomes the product's purchase price (cost of the latest receipt), in the
            // same unit of work as the receipt + movements.
            var repriced = ApplyPurchasePrice(product, input.VariantId, unitCost);
            if (repriced.IsFailure) return Result<string>.Fail(repriced.Error, repriced.Detail);

            var movement = StockMovement.Create(
                context.TenantId, product.Id, input.VariantId, MovementKind.Receipt, quantity, unitCost,
                now, context.UserId, lotNumber: input.LotNumber, expiryUtc: input.ExpiryUtc,
                sourceEntityType: "GoodsReceipt", sourceEntityId: receipt.Id,
                operationId: context.OperationId,
                id: IdGenerator.ForOperation(context.OperationId, MovementSlotBase + i, now));
            if (movement.IsFailure) return Result<string>.Fail(movement.Error, movement.Detail);
            stockMovements.Add(movement.Value!);
        }

        var posted = receipt.Post(clock);
        if (posted.IsFailure) return Result<string>.Fail(posted.Error, posted.Detail);

        if (order is not null)
        {
            var applied = order.ApplyReceipt(receipt);
            if (applied.IsFailure) return Result<string>.Fail(applied.Error, applied.Detail);
        }

        purchasing.AddReceipt(receipt);
        movements.AddRange(stockMovements);
        return Result<string>.Ok(receipt.Id);
    }

    /// <summary>
    /// Sets the received variant's cost to the purchased unit cost (and the product default when it has a
    /// single variant). A zero cost (e.g. a free sample) never wipes an existing purchase price.
    /// </summary>
    private static Result ApplyPurchasePrice(Product product, string variantId, Money unitCost)
    {
        if (unitCost.Minor <= 0L) return Result.Ok();

        for (var i = 0; i < product.Variants.Count; i++)
        {
            var variant = product.Variants[i];
            if (!string.Equals(variant.VariantId, variantId, StringComparison.Ordinal)) continue;
            if (variant.CostPrice == unitCost) break;

            var updated = product.UpdateVariant(variant with { CostPrice = unitCost });
            if (updated.IsFailure) return updated;
            break;
        }

        if (product.Variants.Count == 1 && product.DefaultCostPrice != unitCost)
        {
            return product.SetPrices(unitCost, product.DefaultSalePrice);
        }

        return Result.Ok();
    }

    private static Result EnsureWithinOrder(
        PurchaseOrder order, string productId, string variantId, Quantity quantity, bool allowOverReceipt)
    {
        if (allowOverReceipt) return Result.Ok();

        for (var i = 0; i < order.Lines.Count; i++)
        {
            var line = order.Lines[i];
            if (!string.Equals(line.ProductId, productId, StringComparison.Ordinal)
                || !string.Equals(line.VariantId, variantId, StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.Equals(line.OrderedQuantity.UnitCode, quantity.UnitCode, StringComparison.Ordinal))
                return Result.Fail(ErrorCodes.UnitMismatch,
                    $"Receipt unit '{quantity.UnitCode}' does not match order line unit '{line.OrderedQuantity.UnitCode}'.");

            if (line.ReceivedQuantity.Value + quantity.Value > line.OrderedQuantity.Value)
            {
                return Result.Fail(ErrorCodes.ReceiptExceedsOrder,
                    $"Line '{line.LineId}' would over-receive {$"{line.ReceivedQuantity.Value + quantity.Value}"} of {$"{line.OrderedQuantity.Value}"}.");
            }

            return Result.Ok();
        }

        // A receipt line that matches no order line is a legitimate direct delivery of extra goods.
        return Result.Ok();
    }
}

/// <summary>
/// Adds a supplier to the directory — or returns the existing one with the same (trimmed) name, so
/// «تأمین‌کنندهٔ جدید» typed twice never duplicates. Returns the supplier id.
/// </summary>
[RequiresPermission(PermissionCodes.PurchasingManage)]
[Auditable(AuditCategory.Inventory, CommandTypes.PurchasingSupplierCreate)]
public sealed record CreateSupplierCommand(
    string Name,
    string? Code = null,
    int PaymentTermDays = 0,
    string? Phone = null) : ICommand<string>, IJournaledCommand
{
    public const int MaxNameLength = 128;

    /// <inheritdoc />
    public string CommandTypeName => CommandTypes.PurchasingSupplierCreate;

    /// <inheritdoc />
    public string AggregateTypeName => "Supplier";

    /// <inheritdoc />
    public string? AggregateIdOrNull => null;
}

public sealed class CreateSupplierCommandValidator : IValidator<CreateSupplierCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(CreateSupplierCommand c, CancellationToken ct)
    {
        var failures = new List<ValidationFailure>();
        if (Rules.Required(c.Name, nameof(c.Name)) is { } name) failures.Add(name);
        if (Rules.MaxLength(c.Name, CreateSupplierCommand.MaxNameLength, nameof(c.Name)) is { } length) failures.Add(length);
        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

public sealed class CreateSupplierCommandHandler(ISupplierRepository suppliers, IClock clock)
    : ICommandHandler<CreateSupplierCommand, string>
{
    public async Task<Result<string>> HandleAsync(CreateSupplierCommand c, CommandContext context, CancellationToken ct)
    {
        var name = c.Name.Trim();
        var existing = await suppliers.FindByNameAsync(name, ct);
        if (existing is not null) return Result<string>.Ok(existing.Id);

        var code = string.IsNullOrWhiteSpace(c.Code)
            ? "SUP-" + IdGenerator.CodeSuffix(context.OperationId, 8)
            : c.Code.Trim();
        var created = Supplier.Create(context.TenantId, code, name, c.PaymentTermDays, phone: c.Phone,
            id: IdGenerator.ForOperation(context.OperationId, 0, clock.UtcNow));
        if (created.IsFailure) return Result<string>.Fail(created.Error, created.Detail);

        suppliers.Add(created.Value!);
        return Result<string>.Ok(created.Value!.Id);
    }
}

/// <summary>Abandons a purchase order before anything has arrived. Terminal.</summary>
[RequiresPermission(PermissionCodes.PurchasingManage)]
[Auditable(AuditCategory.Inventory, CommandTypes.PurchasingOrderCancel)]
public sealed record CancelPurchaseOrderCommand(string PurchaseOrderId, string Reason)
    : ICommand<Unit>, IJournaledCommand
{
    /// <inheritdoc />
    public string CommandTypeName => CommandTypes.PurchasingOrderCancel;

    /// <inheritdoc />
    public string AggregateTypeName => "PurchaseOrder";

    /// <inheritdoc />
    public string? AggregateIdOrNull => PurchaseOrderId;
}

public sealed class CancelPurchaseOrderCommandValidator : IValidator<CancelPurchaseOrderCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(CancelPurchaseOrderCommand c, CancellationToken ct)
    {
        var failures = new List<ValidationFailure>();
        if (Rules.Required(c.PurchaseOrderId, nameof(c.PurchaseOrderId)) is { } order) failures.Add(order);
        if (Rules.Required(c.Reason, nameof(c.Reason)) is { } reason) failures.Add(reason);
        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

public sealed class CancelPurchaseOrderCommandHandler(IPurchasingRepository purchasing)
    : ICommandHandler<CancelPurchaseOrderCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(CancelPurchaseOrderCommand c, CommandContext context, CancellationToken ct)
    {
        var order = await purchasing.FindAsync(c.PurchaseOrderId, ct);
        if (order is null) return Result<Unit>.Fail(ErrorCodes.PurchaseOrderNotFound);

        var cancelled = order.Cancel(c.Reason);
        return cancelled.IsFailure
            ? Result<Unit>.Fail(ErrorCodes.PurchaseOrderStateInvalid, cancelled.Detail)
            : Result<Unit>.Ok(Unit.Value);
    }
}
