using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.ValueObjects;

namespace Animora.Domain.Inventory;

/// <summary>Why a stock movement happened. The kind fixes the sign of its quantity.</summary>
public enum MovementKind
{
    /// <summary>Goods received from a supplier. Inbound.</summary>
    Receipt = 0,

    /// <summary>Sold to a client. Outbound.</summary>
    Sale = 1,

    /// <summary>Consumed internally (administered during a visit, used as a consumable). Outbound.</summary>
    Consumption = 2,

    /// <summary>Damaged, expired or otherwise lost. Outbound.</summary>
    Loss = 3,

    /// <summary>Stock-count correction upwards. Inbound.</summary>
    AdjustmentIn = 4,

    /// <summary>Stock-count correction downwards. Outbound.</summary>
    AdjustmentOut = 5,

    /// <summary>Arrived from another location. Inbound.</summary>
    TransferIn = 6,

    /// <summary>Sent to another location. Outbound.</summary>
    TransferOut = 7,

    /// <summary>Returned by a client. Inbound.</summary>
    ReturnIn = 8,

    /// <summary>Returned to a supplier. Outbound.</summary>
    ReturnOut = 9,
}

/// <summary>
/// One entry of the <b>append-only</b> stock ledger — the single authority over stock in the
/// product (ARCHITECTURE.md §11.2, §8.5, R-B23).
/// </summary>
/// <remarks>
/// <para>
/// This type deliberately has <b>no mutator</b>: a movement is written once and never edited or
/// deleted. A correction is a <i>new</i> movement with <see cref="ReversesMovementId"/> pointing at
/// the entry it offsets. That is also what makes inventory conflict resolution additive: two devices
/// that each recorded a movement offline converge by keeping both rows, never by overwriting an
/// absolute quantity.
/// </para>
/// <para>
/// <b>Sign rule (authoritative).</b> <see cref="MovementKind.Receipt"/>,
/// <see cref="MovementKind.AdjustmentIn"/>, <see cref="MovementKind.TransferIn"/> and
/// <see cref="MovementKind.ReturnIn"/> require a strictly positive quantity; every other kind
/// requires a strictly negative one. A zero quantity is never a movement. The rule is enforced by
/// <see cref="Create"/> (<c>MOVEMENT_SIGN_INVALID</c>) — the only way to obtain an instance.
/// </para>
/// <para>
/// It is a <see cref="SyncableEntity"/> and not an aggregate root: it raises no event itself. The
/// writer that appends it (D07/C06) raises <c>StockMovementRecordedEvent</c>.
/// </para>
/// </remarks>
public sealed class StockMovement : SyncableEntity
{
    /// <summary>EF Core materialization constructor. Not for business code.</summary>
    private StockMovement()
    {
    }

    /// <summary>The product the movement belongs to.</summary>
    public string ProductId { get; private init; } = string.Empty;

    /// <summary>The variant the movement applies to — never a bare product id.</summary>
    public string VariantId { get; private init; } = string.Empty;

    /// <summary>Why the movement happened; fixes the sign of <see cref="Quantity"/>.</summary>
    public MovementKind Kind { get; private init; }

    /// <summary>The <b>signed</b> amount moved, in the product's unit. Never zero.</summary>
    public Quantity Quantity { get; private init; }

    /// <summary>Cost per unit at the moment of the movement; feeds the weighted average.</summary>
    public Money UnitCost { get; private init; }

    /// <summary>Supplier lot / batch number; <see langword="null"/> when untracked.</summary>
    public string? LotNumber { get; private init; }

    /// <summary>Expiry of the moved goods, UTC; <see langword="null"/> when non-perishable.</summary>
    public DateTimeOffset? ExpiryUtc { get; private init; }

    /// <summary>When the movement happened, UTC (from the injected clock at the call site).</summary>
    public DateTimeOffset OccurredUtc { get; private init; }

    /// <summary>Type name of the entity that caused it (e.g. <c>GoodsReceipt</c>, <c>Invoice</c>).</summary>
    public string? SourceEntityType { get; private init; }

    /// <summary>Id of the entity that caused it.</summary>
    public string? SourceEntityId { get; private init; }

    /// <summary>
    /// The business command that produced this movement (B02/D07 envelope <c>OperationId</c>), so a
    /// replayed command never double-writes the ledger.
    /// </summary>
    public string? OperationId { get; private init; }

    /// <summary>The staff member who recorded the movement.</summary>
    public string RecordedByUserId { get; private init; } = string.Empty;

    /// <summary>Free-form reason, kept for the audit trail.</summary>
    public string? Reason { get; private init; }

    /// <summary>
    /// The movement this one offsets; <see langword="null"/> for an original entry. A correction is
    /// always a new reversing movement, never an edit.
    /// </summary>
    public string? ReversesMovementId { get; private init; }

    /// <summary><see langword="true"/> when the kind adds to stock.</summary>
    public bool IsInbound => IsInboundKind(Kind);

    /// <summary><see langword="true"/> when this entry offsets an earlier one.</summary>
    public bool IsReversal => ReversesMovementId is not null;

    /// <summary><see langword="true"/> when <paramref name="kind"/> adds to stock.</summary>
    public static bool IsInboundKind(MovementKind kind) => kind
        is MovementKind.Receipt
        or MovementKind.AdjustmentIn
        or MovementKind.TransferIn
        or MovementKind.ReturnIn;

    /// <summary>
    /// The only way to create a movement. Enforces the sign rule for
    /// <paramref name="kind"/> (<c>MOVEMENT_SIGN_INVALID</c>).
    /// </summary>
    /// <param name="tenantId">Owning tenant.</param>
    /// <param name="productId">The product moved.</param>
    /// <param name="variantId">The variant moved.</param>
    /// <param name="kind">Why the movement happened.</param>
    /// <param name="quantity">The signed amount; its sign must match <paramref name="kind"/>.</param>
    /// <param name="unitCost">Cost per unit at this moment.</param>
    /// <param name="occurredUtc">When it happened, UTC.</param>
    /// <param name="recordedByUserId">The staff member recording it.</param>
    /// <param name="lotNumber">Supplier lot / batch number.</param>
    /// <param name="expiryUtc">Expiry of the moved goods, UTC.</param>
    /// <param name="sourceEntityType">Type name of the causing entity.</param>
    /// <param name="sourceEntityId">Id of the causing entity.</param>
    /// <param name="operationId">The business command that produced it.</param>
    /// <param name="reason">Audit-trail reason.</param>
    /// <param name="reversesMovementId">The movement this one offsets, when it is a correction.</param>
    public static Result<StockMovement> Create(
        string tenantId,
        string productId,
        string variantId,
        MovementKind kind,
        Quantity quantity,
        Money unitCost,
        DateTimeOffset occurredUtc,
        string recordedByUserId,
        string? lotNumber = null,
        DateTimeOffset? expiryUtc = null,
        string? sourceEntityType = null,
        string? sourceEntityId = null,
        string? operationId = null,
        string? reason = null,
        string? reversesMovementId = null,
        string? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);
        ArgumentException.ThrowIfNullOrWhiteSpace(variantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(recordedByUserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(quantity.UnitCode);

        var inbound = IsInboundKind(kind);
        if (inbound ? quantity.Value <= 0m : quantity.Value >= 0m)
        {
            return Result<StockMovement>.Fail(
                ErrorCodes.MovementSignInvalid,
                $"{kind} requires a {(inbound ? "positive" : "negative")} quantity; got {quantity.Value}.");
        }

        return new StockMovement
        {
            Id = string.IsNullOrWhiteSpace(id) ? IdGenerator.NewId(occurredUtc) : id,
            TenantId = tenantId,
            ProductId = productId,
            VariantId = variantId,
            Kind = kind,
            Quantity = quantity,
            UnitCost = unitCost,
            LotNumber = Clean(lotNumber),
            ExpiryUtc = expiryUtc,
            OccurredUtc = occurredUtc,
            SourceEntityType = Clean(sourceEntityType),
            SourceEntityId = Clean(sourceEntityId),
            OperationId = Clean(operationId),
            RecordedByUserId = recordedByUserId,
            Reason = Clean(reason),
            ReversesMovementId = Clean(reversesMovementId),
        };
    }

    /// <summary>
    /// Builds the movement that offsets <paramref name="original"/>: the opposite kind, the negated
    /// quantity and <see cref="ReversesMovementId"/> set. The original stays untouched.
    /// </summary>
    public static Result<StockMovement> Reverse(
        StockMovement original,
        DateTimeOffset occurredUtc,
        string recordedByUserId,
        string reason,
        string? operationId = null)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return Create(
            original.TenantId,
            original.ProductId,
            original.VariantId,
            OppositeKind(original.Kind),
            original.Quantity.Negate(),
            original.UnitCost,
            occurredUtc,
            recordedByUserId,
            original.LotNumber,
            original.ExpiryUtc,
            original.SourceEntityType,
            original.SourceEntityId,
            operationId,
            reason,
            original.Id);
    }

    /// <summary>The kind whose sign offsets <paramref name="kind"/>.</summary>
    public static MovementKind OppositeKind(MovementKind kind) => kind switch
    {
        MovementKind.Receipt => MovementKind.ReturnOut,
        MovementKind.Sale => MovementKind.ReturnIn,
        MovementKind.Consumption => MovementKind.AdjustmentIn,
        MovementKind.Loss => MovementKind.AdjustmentIn,
        MovementKind.AdjustmentIn => MovementKind.AdjustmentOut,
        MovementKind.AdjustmentOut => MovementKind.AdjustmentIn,
        MovementKind.TransferIn => MovementKind.TransferOut,
        MovementKind.TransferOut => MovementKind.TransferIn,
        MovementKind.ReturnIn => MovementKind.ReturnOut,
        MovementKind.ReturnOut => MovementKind.ReturnIn,
        _ => MovementKind.AdjustmentOut,
    };

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
