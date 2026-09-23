namespace Animora.Domain.Events;

/// <summary>
/// A stock movement was appended to the ledger. Raised by the movement writer (D07/C06) — the
/// movement itself is a plain append-only row and raises nothing.
/// </summary>
/// <param name="EventId">ULID of the event.</param>
/// <param name="TenantId">Owning tenant.</param>
/// <param name="OccurredAtUtc">When it was raised (informational — A-RULE 011).</param>
/// <param name="MovementId">The appended movement.</param>
/// <param name="ProductId">The product moved.</param>
/// <param name="VariantId">The variant moved — on-hand is always computed per variant.</param>
/// <param name="Kind">Why the movement happened; it fixes the sign of <paramref name="QuantityValue"/>.</param>
/// <param name="QuantityValue">The signed magnitude, in the product's unit.</param>
public sealed record StockMovementRecordedEvent(
    string EventId,
    string TenantId,
    DateTimeOffset OccurredAtUtc,
    string MovementId,
    string ProductId,
    string VariantId,
    Inventory.MovementKind Kind,
    decimal QuantityValue)
    : DomainEvent(EventId, TenantId, OccurredAtUtc);

/// <summary>
/// A variant's derived on-hand reached or fell below its reorder level. Both figures are derived
/// from the ledger by <c>StockCalculator</c> — never read from a stored absolute (R-B23).
/// </summary>
/// <param name="EventId">ULID of the event.</param>
/// <param name="TenantId">Owning tenant.</param>
/// <param name="OccurredAtUtc">When it was raised (informational — A-RULE 011).</param>
/// <param name="ProductId">The product below its reorder point.</param>
/// <param name="VariantId">The variant below its reorder point.</param>
/// <param name="OnHandValue">The derived on-hand magnitude.</param>
/// <param name="ReorderLevelValue">The reorder level it reached.</param>
public sealed record StockBelowReorderEvent(
    string EventId,
    string TenantId,
    DateTimeOffset OccurredAtUtc,
    string ProductId,
    string VariantId,
    decimal OnHandValue,
    decimal ReorderLevelValue)
    : DomainEvent(EventId, TenantId, OccurredAtUtc);

/// <summary>
/// A goods receipt was posted and is now immutable (A-RULE 002). The movement writer turns each of
/// its lines into one <c>MovementKind.Receipt</c> entry.
/// </summary>
/// <param name="EventId">ULID of the event.</param>
/// <param name="TenantId">Owning tenant.</param>
/// <param name="OccurredAtUtc">When it was raised (informational — A-RULE 011).</param>
/// <param name="ReceiptId">The posted receipt; its lines are the movement payload.</param>
/// <param name="SupplierId">The supplier the goods came from.</param>
/// <param name="LineCount">How many movements the writer must append.</param>
public sealed record GoodsReceiptPostedEvent(
    string EventId,
    string TenantId,
    DateTimeOffset OccurredAtUtc,
    string ReceiptId,
    string SupplierId,
    int LineCount)
    : DomainEvent(EventId, TenantId, OccurredAtUtc);

/// <summary>
/// A purchase order was sent to its supplier; its lines are frozen from here on.
/// </summary>
/// <param name="EventId">ULID of the event.</param>
/// <param name="TenantId">Owning tenant.</param>
/// <param name="OccurredAtUtc">When it was raised (informational — A-RULE 011).</param>
/// <param name="OrderId">The submitted order.</param>
/// <param name="SupplierId">The supplier it was sent to.</param>
/// <param name="GrandTotalMinor">Order total in minor units (R-B24 — never a decimal).</param>
/// <param name="Currency">ISO-4217 code of <paramref name="GrandTotalMinor"/>.</param>
public sealed record PurchaseOrderSubmittedEvent(
    string EventId,
    string TenantId,
    DateTimeOffset OccurredAtUtc,
    string OrderId,
    string SupplierId,
    long GrandTotalMinor,
    string Currency)
    : DomainEvent(EventId, TenantId, OccurredAtUtc);
