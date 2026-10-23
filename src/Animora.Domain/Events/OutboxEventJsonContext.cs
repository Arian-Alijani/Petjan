using System.Text.Json.Serialization;

namespace Animora.Domain.Events;

/// <summary>
/// Source-generated (reflection-free) JSON metadata for the finance/inventory events the desktop outbox
/// relay writes and the <c>FinanceProjectionWorker</c> reads back. These are the hot projection path, so
/// resolving their metadata at compile time avoids the per-call reflection cost and keeps the payloads
/// trimming/AOT-ready (CODE_AUDIT_2026-10-07 F-08).
/// <para>
/// No <see cref="JsonSourceGenerationOptionsAttribute"/> is applied, so the generated context uses the
/// same default naming the previous reflection path used — the stored JSON shape is byte-for-byte
/// unchanged and existing outbox rows round-trip exactly.
/// </para>
/// </summary>
[JsonSerializable(typeof(InvoicePostedEvent))]
[JsonSerializable(typeof(InvoiceVoidedEvent))]
[JsonSerializable(typeof(PaymentRegisteredEvent))]
[JsonSerializable(typeof(InvoiceRefundedEvent))]
[JsonSerializable(typeof(GoodsReceiptPostedEvent))]
[JsonSerializable(typeof(StockMovementRecordedEvent))]
[JsonSerializable(typeof(ChequeClearedEvent))]
[JsonSerializable(typeof(ChequeBouncedEvent))]
[JsonSerializable(typeof(CashSessionClosedEvent))]
[JsonSerializable(typeof(ExpensePostedEvent))]
[JsonSerializable(typeof(PetDeletedEvent))]
[JsonSerializable(typeof(OwnerDeletedEvent))]
public sealed partial class OutboxEventJsonContext : JsonSerializerContext
{
}
