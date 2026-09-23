using Animora.Domain.Results;
using Animora.Domain.ValueObjects;

namespace Animora.Domain.Cashier;

/// <summary>Why cash entered or left the drawer. The kind fixes the sign of its amount.</summary>
public enum CashMovementKind
{
    /// <summary>Cash taken for an invoice payment. Inbound.</summary>
    SalePayment = 0,

    /// <summary>Cash handed back to a client as a refund. Outbound.</summary>
    RefundOut = 1,

    /// <summary>Petty-cash spending taken from the drawer. Outbound.</summary>
    PettyCashOut = 2,

    /// <summary>Change float added to the drawer. Inbound.</summary>
    FloatIn = 3,

    /// <summary>Cash removed for a bank deposit. Outbound.</summary>
    BankDropOut = 4,

    /// <summary>Count correction upwards. Inbound.</summary>
    CorrectionIn = 5,

    /// <summary>Count correction downwards. Outbound.</summary>
    CorrectionOut = 6,
}

/// <summary>
/// One signed movement of physical cash inside a <see cref="CashSession"/>: the drawer's ledger
/// entry.
/// </summary>
/// <remarks>
/// <para>
/// <b>Sign rule (authoritative).</b> <see cref="CashMovementKind.SalePayment"/>,
/// <see cref="CashMovementKind.FloatIn"/> and <see cref="CashMovementKind.CorrectionIn"/> require a
/// strictly positive amount; every other kind requires a strictly negative one. A zero amount is
/// never a movement. The rule is enforced by <see cref="Create"/>
/// (<c>MOVEMENT_SIGN_INVALID</c>) — the recommended way to obtain an instance.
/// </para>
/// <para>
/// Movements are never edited: a miscount is corrected with a further
/// <see cref="CashMovementKind.CorrectionIn"/>/<see cref="CashMovementKind.CorrectionOut"/> entry
/// (A-RULE 003), and once the session is closed the correction belongs to the next session.
/// </para>
/// </remarks>
/// <param name="MovementId">ULID identifying the movement inside its session.</param>
/// <param name="Kind">Why the cash moved; fixes the sign of <paramref name="Amount"/>.</param>
/// <param name="Amount">The signed amount; never zero.</param>
/// <param name="InvoiceId">The invoice behind a sale payment or refund.</param>
/// <param name="PaymentId">The payment entry behind it.</param>
/// <param name="Reason">Mandatory audit reason.</param>
/// <param name="OccurredUtc">When the cash moved, UTC, from the injected clock.</param>
/// <param name="RecordedByUserId">The staff member who recorded it.</param>
/// <param name="OperationId">The business command that produced it (A-RULE 006 idempotency key).</param>
public sealed record CashMovement(
    string MovementId,
    CashMovementKind Kind,
    Money Amount,
    string? InvoiceId,
    string? PaymentId,
    string Reason,
    DateTimeOffset OccurredUtc,
    string RecordedByUserId,
    string? OperationId)
{
    /// <summary><see langword="true"/> when <paramref name="kind"/> adds cash to the drawer.</summary>
    public static bool IsInboundKind(CashMovementKind kind) => kind
        is CashMovementKind.SalePayment
        or CashMovementKind.FloatIn
        or CashMovementKind.CorrectionIn;

    /// <summary><see langword="true"/> when this movement adds cash to the drawer.</summary>
    public bool IsInbound => IsInboundKind(Kind);

    /// <summary>
    /// Creates a movement, enforcing the sign rule for <paramref name="kind"/>
    /// (<c>MOVEMENT_SIGN_INVALID</c>).
    /// </summary>
    /// <param name="kind">Why the cash moved.</param>
    /// <param name="amount">The signed amount; its sign must match <paramref name="kind"/>.</param>
    /// <param name="reason">Mandatory audit reason.</param>
    /// <param name="occurredUtc">When the cash moved, UTC.</param>
    /// <param name="recordedByUserId">The staff member recording it.</param>
    /// <param name="invoiceId">The invoice behind a sale payment or refund.</param>
    /// <param name="paymentId">The payment entry behind it.</param>
    /// <param name="operationId">The business command that produced it.</param>
    public static Result<CashMovement> Create(
        CashMovementKind kind,
        Money amount,
        string reason,
        DateTimeOffset occurredUtc,
        string recordedByUserId,
        string? invoiceId = null,
        string? paymentId = null,
        string? operationId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(recordedByUserId);

        var inbound = IsInboundKind(kind);
        if (inbound ? amount.Minor <= 0L : amount.Minor >= 0L)
        {
            return Result<CashMovement>.Fail(
                ErrorCodes.MovementSignInvalid,
                $"{kind} requires a {(inbound ? "positive" : "negative")} amount; got {amount.Minor}.");
        }

        return new CashMovement(
            MovementId: Primitives.IdGenerator.NewId(occurredUtc),
            Kind: kind,
            Amount: amount,
            InvoiceId: Clean(invoiceId),
            PaymentId: Clean(paymentId),
            Reason: reason.Trim(),
            OccurredUtc: occurredUtc,
            RecordedByUserId: recordedByUserId,
            OperationId: Clean(operationId));
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
