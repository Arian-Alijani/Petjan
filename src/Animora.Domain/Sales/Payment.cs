using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.ValueObjects;

namespace Animora.Domain.Sales;

/// <summary>How money moved between the payer and the clinic.</summary>
public enum PaymentMethod
{
    /// <summary>Notes and coins at the counter; requires an open cash session.</summary>
    Cash = 0,

    /// <summary>Card terminal (POS); carries the terminal reference.</summary>
    Card = 1,

    /// <summary>Bank transfer / card-to-card.</summary>
    Transfer = 2,

    /// <summary>A cheque; requires the cheque record (its lifecycle is A08's).</summary>
    Cheque = 3,

    /// <summary>Put on the owner's account to be collected later.</summary>
    Credit = 4,

    /// <summary>Settled by an insurer against the invoice's coverage.</summary>
    Insurance = 5,

    /// <summary>Drawn from the owner's prepaid wallet balance.</summary>
    Wallet = 6,
}

/// <summary>
/// One entry of the <b>append-only</b> payment ledger of an invoice: money that actually moved,
/// recorded once and never changed.
/// </summary>
/// <remarks>
/// <para>
/// <b>[INVARIANT] A payment is never edited or deleted.</b> This type exposes no mutator: every
/// property is <c>private init</c> and <see cref="Create"/> is the only way to obtain an instance.
/// A reversal is a <i>new</i> payment with a negative <see cref="Amount"/> and
/// <see cref="ReversesPaymentId"/> pointing at the entry it offsets (A-RULE 002/003) — the same
/// compensating-entry discipline the stock ledger uses, and what lets two offline devices converge
/// by keeping both rows instead of overwriting a balance.
/// </para>
/// <para>
/// <b>Idempotency.</b> <see cref="OperationId"/> is the business command that produced the payment
/// (A-RULE 006). <see cref="Invoice.ApplyPayment"/> refuses a second payment carrying an
/// <see cref="OperationId"/> already applied to that invoice (<c>PAYMENT_DUPLICATE_OPERATION</c>),
/// so a replayed command is a no-op rather than a second financial effect.
/// </para>
/// <para>
/// <b>Method references.</b> A <see cref="PaymentMethod.Cash"/> payment must name the cash session
/// it belongs to and a <see cref="PaymentMethod.Cheque"/> payment must name its cheque, otherwise
/// the drawer could never be reconciled (<c>PAYMENT_METHOD_REFERENCE_REQUIRED</c>). Both are
/// enforced by <see cref="Create"/>.
/// </para>
/// <para>
/// It is a <see cref="SyncableEntity"/> and not an aggregate root: it raises no event itself — the
/// invoice raises <c>PaymentRegisteredEvent</c> when it applies the payment.
/// </para>
/// </remarks>
public sealed class Payment : SyncableEntity
{
    /// <summary>EF Core materialization constructor. Not for business code.</summary>
    private Payment()
    {
    }

    /// <summary>The invoice the money was received against.</summary>
    public string InvoiceId { get; private init; } = string.Empty;

    /// <summary>How the money moved.</summary>
    public PaymentMethod Method { get; private init; }

    /// <summary>
    /// The signed amount: positive for a receipt, negative for a refund — the compensating entry
    /// that offsets an earlier receipt. Never zero.
    /// </summary>
    public Money Amount { get; private init; }

    /// <summary>When the money moved, UTC (from the injected clock at the call site).</summary>
    public DateTimeOffset ReceivedUtc { get; private init; }

    /// <summary>The staff member who took the payment.</summary>
    public string ReceivedByUserId { get; private init; } = string.Empty;

    /// <summary>The cash session the drawer movement belongs to; required for cash.</summary>
    public string? CashSessionId { get; private init; }

    /// <summary>The cheque record backing this payment; required for a cheque.</summary>
    public string? ChequeId { get; private init; }

    /// <summary>POS / terminal reference for a card payment, kept for reconciliation.</summary>
    public string? CardReference { get; private init; }

    /// <summary>The business command that produced it — the idempotency key (A-RULE 006).</summary>
    public string? OperationId { get; private init; }

    /// <summary>Where this payment's command stands with the server (ARCHITECTURE.md §8.3).</summary>
    public SettlementStatus Settlement { get; private init; }

    /// <summary>Free-form note, kept for the audit trail.</summary>
    public string? Note { get; private init; }

    /// <summary>
    /// The payment this one offsets; <see langword="null"/> for an original receipt. A correction
    /// is always a new reversing payment, never an edit.
    /// </summary>
    public string? ReversesPaymentId { get; private init; }

    /// <summary><see langword="true"/> when money was returned to the payer.</summary>
    public bool IsRefund => Amount.IsNegative;

    /// <summary><see langword="true"/> when this entry offsets an earlier payment.</summary>
    public bool IsReversal => ReversesPaymentId is not null;

    /// <summary>
    /// The only way to create a payment. Refuses a zero amount and enforces the per-method
    /// reference rule (<c>PAYMENT_METHOD_REFERENCE_REQUIRED</c>).
    /// </summary>
    /// <param name="tenantId">Owning tenant.</param>
    /// <param name="invoiceId">The invoice being paid.</param>
    /// <param name="method">How the money moved.</param>
    /// <param name="amount">Signed amount: positive receipt, negative refund. Never zero.</param>
    /// <param name="receivedUtc">When the money moved, UTC.</param>
    /// <param name="receivedByUserId">The staff member taking the payment.</param>
    /// <param name="cashSessionId">The open cash session; mandatory for <see cref="PaymentMethod.Cash"/>.</param>
    /// <param name="chequeId">The cheque record; mandatory for <see cref="PaymentMethod.Cheque"/>.</param>
    /// <param name="cardReference">POS/terminal reference for a card payment.</param>
    /// <param name="operationId">The business command that produced it (idempotency key).</param>
    /// <param name="settlement">Initial settlement state; defaults to <see cref="SettlementStatus.LocallyPosted"/>.</param>
    /// <param name="note">Audit-trail note.</param>
    /// <param name="reversesPaymentId">The payment this one offsets, when it is a reversal.</param>
    public static Result<Payment> Create(
        string tenantId,
        string invoiceId,
        PaymentMethod method,
        Money amount,
        DateTimeOffset receivedUtc,
        string receivedByUserId,
        string? cashSessionId = null,
        string? chequeId = null,
        string? cardReference = null,
        string? operationId = null,
        SettlementStatus settlement = SettlementStatus.LocallyPosted,
        string? note = null,
        string? reversesPaymentId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(invoiceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(receivedByUserId);

        if (amount.IsZero)
        {
            return Result<Payment>.Fail(
                ErrorCodes.ValidationFailed,
                $"A payment on invoice '{invoiceId}' must move a non-zero amount.");
        }

        if (method == PaymentMethod.Cash && string.IsNullOrWhiteSpace(cashSessionId))
        {
            return Result<Payment>.Fail(
                ErrorCodes.PaymentMethodReferenceRequired,
                "A cash payment must name the cash session its drawer movement belongs to.");
        }

        if (method == PaymentMethod.Cheque && string.IsNullOrWhiteSpace(chequeId))
        {
            return Result<Payment>.Fail(
                ErrorCodes.PaymentMethodReferenceRequired,
                "A cheque payment must name its cheque record.");
        }

        return new Payment
        {
            Id = IdGenerator.NewId(receivedUtc),
            TenantId = tenantId,
            InvoiceId = invoiceId,
            Method = method,
            Amount = amount,
            ReceivedUtc = receivedUtc,
            ReceivedByUserId = receivedByUserId,
            CashSessionId = Clean(cashSessionId),
            ChequeId = Clean(chequeId),
            CardReference = Clean(cardReference),
            OperationId = Clean(operationId),
            Settlement = settlement,
            Note = Clean(note),
            ReversesPaymentId = Clean(reversesPaymentId),
        };
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
