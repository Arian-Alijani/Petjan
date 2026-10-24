using Animora.Domain.Events;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Domain.ValueObjects;

namespace Animora.Domain.Sales;

/// <summary>What the invoice bills for.</summary>
public enum InvoiceKind
{
    /// <summary>Raised from a clinical visit's billable lines.</summary>
    Visit = 0,

    /// <summary>Raised from a boarding stay at checkout.</summary>
    Boarding = 1,

    /// <summary>A counter sale with no encounter behind it.</summary>
    DirectSale = 2,

    /// <summary>Goods came back from the client; the economics run the other way.</summary>
    SalesReturn = 3,
}

/// <summary>Lifecycle state of an invoice.</summary>
public enum InvoiceState
{
    /// <summary>Being composed; lines, discount and insurance may still change.</summary>
    Draft = 0,

    /// <summary>Issued to the client. From here on the invoice is immutable (A-RULE 002).</summary>
    Posted = 1,

    /// <summary>Some, but not all, of the due amount has been received.</summary>
    PartiallyPaid = 2,

    /// <summary>Fully settled by the payer.</summary>
    Paid = 3,

    /// <summary>Cancelled before any money moved. Terminal.</summary>
    Void = 4,

    /// <summary>Money was given back through a refund adjustment. Terminal.</summary>
    Refunded = 5,
}

/// <summary>
/// The invoice aggregate root: the money-facing core of the product. It owns its lines, its
/// discount, tax and insurance economics, its payment tally and the adjustments written against it.
/// </summary>
/// <remarks>
/// <para>The state machine is the only authority over what may change and when:</para>
/// <code>
/// Draft         -> Posted | Void
/// Posted        -> PartiallyPaid | Paid | Void
/// PartiallyPaid -> Paid | Refunded
/// Paid          -> Refunded
/// Void / Refunded -> (terminal)
/// </code>
/// <para>
/// <b>[INVARIANT] Immutability after posting (A-RULE 002).</b> Once <see cref="State"/> leaves
/// <see cref="InvoiceState.Draft"/>, <see cref="AddLine"/>, <see cref="RemoveLine"/>,
/// <see cref="EditLine"/>, <see cref="SetDiscount"/> and <see cref="SetInsurance"/> all return
/// <c>INVOICE_IMMUTABLE</c>. A change after posting is expressed as an
/// <see cref="InvoiceAdjustment"/> — a credit note or reversal — never as an edit. Voiding is
/// allowed from <see cref="InvoiceState.Draft"/> and <see cref="InvoiceState.Posted"/> only while
/// nothing has been paid; a paid invoice is refunded instead
/// (<c>INVOICE_VOID_AFTER_PAYMENT</c>).
/// </para>
/// <para>
/// <b>Totals.</b> The invoice never accepts totals from outside: every mutator calls
/// <see cref="RecalculateTotals"/>, and <see cref="Post"/> re-verifies that the stored totals still
/// equal what <see cref="InvoiceTotals"/> computes (<c>INVOICE_TOTAL_MISMATCH</c>) and that the
/// grand total is not negative (<c>INVOICE_NEGATIVE_TOTAL</c>).
/// </para>
/// <para>
/// <b>Idempotency (A-RULE 006).</b> <see cref="ApplyPayment"/> rejects a payment whose
/// <see cref="Payment.OperationId"/> was already applied to this invoice
/// (<c>PAYMENT_DUPLICATE_OPERATION</c>) and leaves <see cref="PaidTotal"/> untouched, so replaying
/// a command has no second financial effect. <see cref="Settlement"/> is carried on the aggregate
/// so the UI can always show where the command stands (ARCHITECTURE.md §8.3) — it is never hidden.
/// </para>
/// </remarks>
public sealed class Invoice : AggregateRoot
{
    private readonly List<InvoiceLine> _lines = [];
    private readonly List<InvoiceAdjustment> _adjustments = [];
    private readonly List<string> _appliedPaymentOperationIds = [];

    /// <summary>EF Core materialization constructor. Not for business code.</summary>
    private Invoice()
    {
    }

    /// <summary>Per-tenant sequential invoice number, assigned by the Application layer (B01) — never here.</summary>
    public string InvoiceNumber { get; private set; } = string.Empty;

    /// <summary>What the invoice bills for.</summary>
    public InvoiceKind Kind { get; private set; }

    /// <summary>Current lifecycle state.</summary>
    public InvoiceState State { get; private set; } = InvoiceState.Draft;

    /// <summary>The party billed; <see langword="null"/> for a walk-in guest sale.</summary>
    public string? OwnerId { get; private set; }

    /// <summary>The patient the charges relate to, when there is one.</summary>
    public string? PetId { get; private set; }

    /// <summary>The visit this invoice bills, when <see cref="Kind"/> is <see cref="InvoiceKind.Visit"/>.</summary>
    public string? VisitId { get; private set; }

    /// <summary>The stay this invoice bills, when <see cref="Kind"/> is <see cref="InvoiceKind.Boarding"/>.</summary>
    public string? BoardingStayId { get; private set; }

    /// <summary>Name typed for a walk-in with no owner record.</summary>
    public string? GuestName { get; private set; }

    /// <summary>When the invoice was raised, UTC.</summary>
    public DateTimeOffset IssuedUtc { get; private set; }

    /// <summary>When it was posted, UTC; <see langword="null"/> while it is a draft.</summary>
    public DateTimeOffset? PostedUtc { get; private set; }

    /// <summary>When it was fully paid, UTC; <see langword="null"/> until then.</summary>
    public DateTimeOffset? SettledUtc { get; private set; }

    /// <summary>Σ of the lines' gross amounts, before any discount or tax.</summary>
    public Money Subtotal { get; private set; }

    /// <summary>How <see cref="DiscountValue"/> is to be read.</summary>
    public DiscountKind DiscountKind { get; private set; }

    /// <summary>A percentage, or a fixed amount in minor units, per <see cref="DiscountKind"/>.</summary>
    public decimal DiscountValue { get; private set; }

    /// <summary>Line discounts plus the invoice-level discount.</summary>
    public Money DiscountTotal { get; private set; }

    /// <summary>Tax on the line net after both the line and the pro-rated invoice discount.</summary>
    public Money TaxTotal { get; private set; }

    /// <summary>The insurer covering part of this invoice; <see langword="null"/> when uninsured.</summary>
    public string? InsurerId { get; private set; }

    /// <summary>The insurer's share, in percent, of the taxed discounted total.</summary>
    public decimal InsurancePercent { get; private set; }

    /// <summary>The amount the insurer covers; deducted from <see cref="GrandTotal"/>.</summary>
    public Money InsuranceCoverage { get; private set; }

    /// <summary><c>Subtotal - DiscountTotal + TaxTotal - InsuranceCoverage</c>: what the payer owes in full.</summary>
    public Money GrandTotal { get; private set; }

    /// <summary>Σ of the applied payments, signed (a refund subtracts).</summary>
    public Money PaidTotal { get; private set; }

    /// <summary><c>GrandTotal - PaidTotal - Σ Adjustments</c>: what is still outstanding.</summary>
    public Money DueTotal { get; private set; }

    /// <summary>Where this invoice's command stands with the server (ARCHITECTURE.md §8.3).</summary>
    public SettlementStatus Settlement { get; private set; } = SettlementStatus.Draft;

    /// <summary>The business command that produced the invoice — the idempotency key (A-RULE 006).</summary>
    public string? OperationId { get; private set; }

    /// <summary>The staff member who raised the invoice.</summary>
    public string CreatedByUserId { get; private set; } = string.Empty;

    /// <summary>Free-form notes.</summary>
    public string? Notes { get; private set; }

    /// <summary>Error code explaining a <see cref="SettlementStatus.Rejected"/> or stuck settlement.</summary>
    public string? SettlementErrorCode { get; private set; }

    /// <summary>The charged lines; frozen from <see cref="InvoiceState.Posted"/> on.</summary>
    public IReadOnlyList<InvoiceLine> Lines => _lines;

    /// <summary>The compensating entries written against this invoice — the only post-posting change path.</summary>
    public IReadOnlyList<InvoiceAdjustment> Adjustments => _adjustments;

    /// <summary><see langword="true"/> once nothing about the invoice's economics may be edited.</summary>
    public bool IsImmutable => State != InvoiceState.Draft;

    /// <summary>Currency every amount on this invoice is expressed in.</summary>
    public string Currency => GrandTotal.Currency;

    /// <summary>Creates an empty invoice in <see cref="InvoiceState.Draft"/>.</summary>
    /// <param name="tenantId">Owning tenant.</param>
    /// <param name="invoiceNumber">Per-tenant sequential number, assigned by the Application layer.</param>
    /// <param name="kind">What the invoice bills for.</param>
    /// <param name="issuedUtc">When the invoice is raised, UTC.</param>
    /// <param name="createdByUserId">The staff member raising it.</param>
    /// <param name="ownerId">The party billed; <see langword="null"/> for a guest sale.</param>
    /// <param name="guestName">Name of the walk-in, when there is no owner record.</param>
    /// <param name="petId">The patient the charges relate to.</param>
    /// <param name="currency">ISO-4217 code every amount on the invoice uses.</param>
    /// <param name="operationId">The business command that produced it (idempotency key).</param>
    /// <param name="notes">Free-form notes.</param>
    /// <param name="id">
    /// Explicit aggregate id. The command layer passes the creating command's <c>OperationId</c> so the offline client and the
    /// server (which both run the same handler for the same operation) derive the <b>same</b> invoice id — follow-up commands
    /// (add line, owner, discount, insurance, post) address the invoice by id and would otherwise hit a server that minted a
    /// different one (empty / rejected invoice). <see langword="null"/> mints a fresh ULID.
    /// </param>
    public static Result<Invoice> Create(
        string tenantId,
        string invoiceNumber,
        InvoiceKind kind,
        DateTimeOffset issuedUtc,
        string createdByUserId,
        string? ownerId = null,
        string? guestName = null,
        string? petId = null,
        string currency = CurrencyScale.Irt,
        string? operationId = null,
        string? notes = null,
        string? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(invoiceNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(createdByUserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);

        if (string.IsNullOrWhiteSpace(ownerId) && string.IsNullOrWhiteSpace(guestName))
        {
            return Result<Invoice>.Fail(
                ErrorCodes.ValidationFailed,
                "An invoice must name either an owner or a guest.");
        }

        var zero = Money.Zero(currency);
        return new Invoice
        {
            Id = string.IsNullOrWhiteSpace(id) ? IdGenerator.NewId(issuedUtc) : id.Trim(),
            TenantId = tenantId,
            InvoiceNumber = invoiceNumber.Trim(),
            Kind = kind,
            State = InvoiceState.Draft,
            OwnerId = Clean(ownerId),
            GuestName = Clean(guestName),
            PetId = Clean(petId),
            IssuedUtc = issuedUtc,
            Subtotal = zero,
            DiscountKind = DiscountKind.Percent,
            DiscountValue = 0m,
            DiscountTotal = zero,
            TaxTotal = zero,
            InsurancePercent = 0m,
            InsuranceCoverage = zero,
            GrandTotal = zero,
            PaidTotal = zero,
            DueTotal = zero,
            Settlement = SettlementStatus.Draft,
            OperationId = Clean(operationId),
            CreatedByUserId = createdByUserId,
            Notes = Clean(notes),
        };
    }

    // ---- draft-only mutators (all guarded by EnsureDraft) -------------------

    /// <summary>Appends a line and recomputes the totals. Refused once posted (<c>INVOICE_IMMUTABLE</c>).</summary>
    public Result AddLine(InvoiceLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        var guard = EnsureDraft();
        if (guard.IsFailure)
        {
            return guard;
        }

        if (line.Quantity.Value <= 0m)
        {
            return Result.Fail(
                ErrorCodes.ValidationFailed,
                $"Line '{line.LineId}' must charge a positive quantity; got {line.Quantity.Value}.");
        }

        if (!string.Equals(line.UnitPrice.Currency, Currency, StringComparison.Ordinal))
        {
            return Result.Fail(
                ErrorCodes.ValidationFailed,
                $"Line currency '{line.UnitPrice.Currency}' does not match invoice currency '{Currency}'.");
        }

        _lines.Add(line);
        RecalculateTotals();
        return Result.Ok();
    }

    /// <summary>Removes a line by its id and recomputes the totals. Refused once posted.</summary>
    public Result RemoveLine(string lineId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lineId);

        var guard = EnsureDraft();
        if (guard.IsFailure)
        {
            return guard;
        }

        var index = IndexOf(lineId);
        if (index < 0)
        {
            return Result.Fail(ErrorCodes.NotFound, $"Line '{lineId}' is not on invoice '{Id}'.");
        }

        _lines.RemoveAt(index);
        RecalculateTotals();
        return Result.Ok();
    }

    /// <summary>
    /// Replaces a line in place (same <see cref="InvoiceLine.LineId"/>) and recomputes the totals.
    /// Refused once posted — after posting, a line change is an <see cref="InvoiceAdjustment"/>.
    /// </summary>
    public Result EditLine(InvoiceLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        var guard = EnsureDraft();
        if (guard.IsFailure)
        {
            return guard;
        }

        var index = IndexOf(line.LineId);
        if (index < 0)
        {
            return Result.Fail(ErrorCodes.NotFound, $"Line '{line.LineId}' is not on invoice '{Id}'.");
        }

        if (line.Quantity.Value <= 0m)
        {
            return Result.Fail(
                ErrorCodes.ValidationFailed,
                $"Line '{line.LineId}' must charge a positive quantity; got {line.Quantity.Value}.");
        }

        if (!string.Equals(line.UnitPrice.Currency, Currency, StringComparison.Ordinal))
        {
            return Result.Fail(
                ErrorCodes.ValidationFailed,
                $"Line currency '{line.UnitPrice.Currency}' does not match invoice currency '{Currency}'.");
        }

        _lines[index] = line;
        RecalculateTotals();
        return Result.Ok();
    }

    /// <summary>
    /// Sets the invoice-level discount and recomputes the totals. Refused once posted; a negative
    /// value is never a discount.
    /// </summary>
    public Result SetDiscount(DiscountKind kind, decimal value)
    {
        var guard = EnsureDraft();
        if (guard.IsFailure)
        {
            return guard;
        }

        if (value < 0m)
        {
            return Result.Fail(
                ErrorCodes.ValidationFailed,
                $"A discount value must not be negative; got {value}.");
        }

        if (kind == DiscountKind.Percent && value > 100m)
        {
            return Result.Fail(
                ErrorCodes.ValidationFailed,
                $"A percentage discount must be between 0 and 100; got {value}.");
        }

        DiscountKind = kind;
        DiscountValue = value;
        RecalculateTotals();
        return Result.Ok();
    }

    /// <summary>
    /// Sets the insurer and its coverage percentage, then recomputes the totals. Refused once
    /// posted; a coverage outside 0–100% is rejected.
    /// </summary>
    public Result SetInsurance(string? insurerId, decimal percent)
    {
        var guard = EnsureDraft();
        if (guard.IsFailure)
        {
            return guard;
        }

        if (percent is < 0m or > 100m)
        {
            return Result.Fail(
                ErrorCodes.ValidationFailed,
                $"Insurance coverage must be between 0 and 100 percent; got {percent}.");
        }

        if (string.IsNullOrWhiteSpace(insurerId) && percent > 0m)
        {
            return Result.Fail(
                ErrorCodes.ValidationFailed,
                "A non-zero insurance coverage must name the insurer.");
        }

        InsurerId = Clean(insurerId);
        InsurancePercent = percent;
        RecalculateTotals();
        return Result.Ok();
    }

    /// <summary>Links the visit this invoice bills. Refused once posted.</summary>
    public Result AttachVisit(string visitId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(visitId);

        var guard = EnsureDraft();
        if (guard.IsFailure)
        {
            return guard;
        }

        VisitId = visitId;
        return Result.Ok();
    }

    /// <summary>Links the boarding stay this invoice bills. Refused once posted.</summary>
    public Result AttachStay(string boardingStayId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(boardingStayId);

        var guard = EnsureDraft();
        if (guard.IsFailure)
        {
            return guard;
        }

        BoardingStayId = boardingStayId;
        return Result.Ok();
    }

    /// <summary>
    /// Attaches the owner record a guest sale turned out to belong to. Refused once posted: after
    /// posting, the billed party is part of the issued document.
    /// </summary>
    public Result AssignOwner(string ownerId, string? petId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);

        var guard = EnsureDraft();
        if (guard.IsFailure)
        {
            return guard;
        }

        OwnerId = ownerId;
        PetId = Clean(petId) ?? PetId;
        GuestName = null;
        return Result.Ok();
    }

    /// <summary>
    /// Detaches the owner record and bills a walk-in («مشتری گذری») instead — used when the wrong customer was picked.
    /// Refused once posted.
    /// </summary>
    public Result AssignGuest(string guestName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(guestName);

        var guard = EnsureDraft();
        if (guard.IsFailure)
        {
            return guard;
        }

        OwnerId = null;
        PetId = null;
        GuestName = Clean(guestName);
        return Result.Ok();
    }

    // ---- lifecycle ----------------------------------------------------------

    /// <summary>Replaces a draft's provisional number with the transactional sequence before posting.</summary>
    public Result AssignInvoiceNumber(string number)
    {
        if (string.IsNullOrWhiteSpace(number)) return Result.Fail(ErrorCodes.ValidationFailed);
        var guard = EnsureDraft();
        if (guard.IsFailure) return guard;
        InvoiceNumber = number.Trim();
        return Result.Ok();
    }

    /// <summary>
    /// Stamps the gapless sequential number onto an invoice that has <b>just</b> been posted in this same
    /// unit of work, still carries its device-provisional (<c>draft-…</c>) number and has had no money or
    /// adjustment applied yet. This is the identical number-finalization <see cref="AssignInvoiceNumber"/>
    /// performs on a draft, deliberately moved to run <i>after</i> <see cref="Post"/> so the Application
    /// layer can allocate the serialized <c>DocumentCounters</c> value at the very end of the command
    /// transaction (shrinking the counter row-lock hold window) without re-opening the draft.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A-RULE 002 is preserved: this touches only the still-provisional identity field, never the frozen
    /// economics (lines, discount, tax, insurance, totals). It is refused once the invoice carries a
    /// real number — <see cref="State"/> is not <see cref="InvoiceState.Posted"/>, a payment or adjustment
    /// has already landed, or the number is no longer the provisional placeholder — so it can never rewrite
    /// a settled or server-adopted number (that is <see cref="AdoptServerInvoiceNumber"/>'s sole job).
    /// </para>
    /// </remarks>
    public Result FinalizePostedInvoiceNumber(string number)
    {
        if (string.IsNullOrWhiteSpace(number)) return Result.Fail(ErrorCodes.ValidationFailed);
        if (State != InvoiceState.Posted)
            return Result.Fail(ErrorCodes.InvoiceInvalidTransition,
                $"Invoice '{Id}' is {State}; a sequential number is finalized only on a freshly posted invoice.");
        if (!PaidTotal.IsZero || _adjustments.Count > 0)
            return Result.Fail(ErrorCodes.InvoiceImmutable,
                $"Invoice '{Id}' already has money or an adjustment applied; its number is fixed.");
        if (!InvoiceNumber.StartsWith("draft-", StringComparison.Ordinal))
            return Result.Fail(ErrorCodes.InvoiceImmutable,
                $"Invoice '{Id}' already carries a sequential number; it is not re-numbered.");
        InvoiceNumber = number.Trim();
        return Result.Ok();
    }

    /// <summary>
    /// Issues the invoice: freezes it, stamps <see cref="PostedUtc"/> and raises
    /// <see cref="InvoicePostedEvent"/>. Refused for an empty invoice (<c>INVOICE_EMPTY</c>), when
    /// the stored totals disagree with a recomputation (<c>INVOICE_TOTAL_MISMATCH</c>) and when the
    /// grand total is negative (<c>INVOICE_NEGATIVE_TOTAL</c>).
    /// </summary>
    public Result Post(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (_lines.Count == 0)
        {
            return Result.Fail(ErrorCodes.InvoiceEmpty, $"Invoice '{Id}' has no lines to post.");
        }

        var totals = EnsureTotalsMatch();
        if (totals.IsFailure)
        {
            return totals;
        }

        if (GrandTotal.IsNegative)
        {
            return Result.Fail(
                ErrorCodes.InvoiceNegativeTotal,
                $"Invoice '{Id}' has a negative grand total {GrandTotal.Minor}.");
        }

        // A zero grand total has lines but no money to recognise. Balanced double-entry accounting cannot
        // record it — the ledger rule drops every zero-amount line and is then left with a journal entry
        // of zero lines (JOURNAL_TOO_FEW_LINES). Refuse it here, at the domain invariant, with a precise
        // operator-facing code instead of letting it fail deep in the server's posting layer and jam sync.
        if (GrandTotal.Minor == 0L)
        {
            return Result.Fail(
                ErrorCodes.InvoiceNonPositiveTotal,
                $"Invoice '{Id}' has a zero grand total; price the lines before posting.");
        }

        var moved = Transition(InvoiceState.Posted);
        if (moved.IsFailure)
        {
            return moved;
        }

        var now = clock.UtcNow;
        PostedUtc = now;
        Settlement = SettlementStatus.LocallyPosted;

        Raise(new InvoicePostedEvent(
            IdGenerator.NewId(now),
            TenantId,
            now,
            Id,
            OwnerId,
            GrandTotal.Minor,
            GrandTotal.Currency,
            VisitId,
            BoardingStayId));
        return Result.Ok();
    }

    /// <summary>
    /// Applies a payment to this invoice: tallies it into <see cref="PaidTotal"/>, advances the
    /// state to <see cref="InvoiceState.PartiallyPaid"/> or <see cref="InvoiceState.Paid"/> and
    /// raises <see cref="PaymentRegisteredEvent"/>.
    /// </summary>
    /// <remarks>
    /// Refuses a payment for another invoice, a payment carrying an <see cref="Payment.OperationId"/>
    /// already applied here (<c>PAYMENT_DUPLICATE_OPERATION</c>, leaving <see cref="PaidTotal"/>
    /// untouched — A-RULE 006) and a receipt exceeding <see cref="DueTotal"/>
    /// (<c>PAYMENT_EXCEEDS_DUE</c>). A negative amount (a refund's compensating entry) is applied
    /// without the due check: the matching <see cref="InvoiceAdjustment"/> already authorized it.
    /// </remarks>
    public Result ApplyPayment(Payment payment)
    {
        ArgumentNullException.ThrowIfNull(payment);

        if (!string.Equals(payment.InvoiceId, Id, StringComparison.Ordinal))
        {
            return Result.Fail(
                ErrorCodes.ValidationFailed,
                $"Payment '{payment.Id}' belongs to invoice '{payment.InvoiceId}', not '{Id}'.");
        }

        if (State is InvoiceState.Draft or InvoiceState.Void or InvoiceState.Refunded)
        {
            return Result.Fail(
                ErrorCodes.InvoiceInvalidTransition,
                $"Invoice '{Id}' is {State}; a payment can only be applied to a posted invoice.");
        }

        if (payment.OperationId is { } operationId
            && _appliedPaymentOperationIds.Contains(operationId, StringComparer.Ordinal))
        {
            return Result.Fail(
                ErrorCodes.PaymentDuplicateOperation,
                $"Operation '{operationId}' was already applied to invoice '{Id}'; replay has no second effect.");
        }

        if (!payment.Amount.IsNegative && payment.Amount > DueTotal)
        {
            return Result.Fail(
                ErrorCodes.PaymentExceedsDue,
                $"Payment {payment.Amount.Minor} exceeds the {DueTotal.Minor} due on invoice '{Id}'.");
        }

        PaidTotal = PaidTotal.Add(payment.Amount);
        RecalculateTotals();

        if (payment.OperationId is { } applied)
        {
            _appliedPaymentOperationIds.Add(applied);
        }

        var target = DueTotal.Minor <= 0L ? InvoiceState.Paid : InvoiceState.PartiallyPaid;
        if (target != State)
        {
            var moved = Transition(target);
            if (moved.IsFailure)
            {
                return moved;
            }
        }

        if (State == InvoiceState.Paid)
        {
            SettledUtc = payment.ReceivedUtc;
        }

        Raise(new PaymentRegisteredEvent(
            IdGenerator.NewId(payment.ReceivedUtc),
            TenantId,
            payment.ReceivedUtc,
            payment.Id,
            Id,
            payment.Amount.Minor,
            payment.Amount.Currency,
            payment.Method));
        return Result.Ok();
    }

    /// <summary>
    /// Records a refund against a paid or partially paid invoice: appends the adjustment, moves the
    /// invoice to <see cref="InvoiceState.Refunded"/> and raises <see cref="InvoiceRefundedEvent"/>.
    /// The money leaving the drawer is a separate negative <see cref="Payment"/>.
    /// </summary>
    /// <remarks>
    /// The refunded amount may not exceed what was actually received
    /// (<c>ADJUSTMENT_EXCEEDS_PAID</c>) — the clinic never returns money it was not given.
    /// </remarks>
    public Result ApplyRefund(InvoiceAdjustment adjustment)
    {
        ArgumentNullException.ThrowIfNull(adjustment);

        if (adjustment.Kind != AdjustmentKind.Refund)
        {
            return Result.Fail(
                ErrorCodes.ValidationFailed,
                $"Adjustment '{adjustment.AdjustmentId}' is a {adjustment.Kind}, not a refund.");
        }

        if (adjustment.Amount.Abs() > PaidTotal)
        {
            return Result.Fail(
                ErrorCodes.AdjustmentExceedsPaid,
                $"Refund {adjustment.Amount.Abs().Minor} exceeds the {PaidTotal.Minor} received on invoice '{Id}'.");
        }

        var moved = Transition(InvoiceState.Refunded);
        if (moved.IsFailure)
        {
            return moved;
        }

        _adjustments.Add(adjustment);
        RecalculateTotals();
        Settlement = SettlementStatus.Reversed;

        Raise(new InvoiceRefundedEvent(
            IdGenerator.NewId(adjustment.CreatedUtc),
            TenantId,
            adjustment.CreatedUtc,
            Id,
            adjustment.AdjustmentId,
            adjustment.Amount.Minor,
            adjustment.Amount.Currency));
        return Result.Ok();
    }

    /// <summary>Records the refund adjustment and its compensating negative payment atomically.
    /// The original payment rows remain append-only; the paid tally reflects the cash returned.</summary>
    public Result ApplyRefund(InvoiceAdjustment adjustment, Payment refundPayment)
    {
        ArgumentNullException.ThrowIfNull(adjustment);
        ArgumentNullException.ThrowIfNull(refundPayment);
        if (refundPayment.InvoiceId != Id || !refundPayment.Amount.IsNegative ||
            refundPayment.Amount != adjustment.Amount || refundPayment.Amount.Currency != Currency)
            return Result.Fail(ErrorCodes.ValidationFailed);
        if (refundPayment.OperationId is { } op && _appliedPaymentOperationIds.Contains(op, StringComparer.Ordinal))
            return Result.Fail(ErrorCodes.PaymentDuplicateOperation);
        var result = ApplyRefund(adjustment);
        if (result.IsFailure) return result;
        PaidTotal = PaidTotal.Add(refundPayment.Amount);
        RecalculateTotals();
        if (refundPayment.OperationId is { } applied) _appliedPaymentOperationIds.Add(applied);
        Raise(new PaymentRegisteredEvent(IdGenerator.NewId(refundPayment.ReceivedUtc), TenantId,
            refundPayment.ReceivedUtc, refundPayment.Id, Id, refundPayment.Amount.Minor,
            refundPayment.Amount.Currency, refundPayment.Method));
        return Result.Ok();
    }

    /// <summary>
    /// Appends a non-refund adjustment (credit note, write-off, rounding correction, line return) —
    /// the only way a posted invoice's economics change (A-RULE 002). The state is unchanged; only
    /// <see cref="DueTotal"/> moves.
    /// </summary>
    public Result AddCreditNote(InvoiceAdjustment adjustment)
    {
        ArgumentNullException.ThrowIfNull(adjustment);

        if (adjustment.Kind == AdjustmentKind.Refund)
        {
            return Result.Fail(
                ErrorCodes.ValidationFailed,
                $"Adjustment '{adjustment.AdjustmentId}' is a refund; use {nameof(ApplyRefund)}.");
        }

        if (State == InvoiceState.Draft)
        {
            return Result.Fail(
                ErrorCodes.InvoiceInvalidTransition,
                $"Invoice '{Id}' is a draft; edit it instead of adjusting it.");
        }

        if (State is InvoiceState.Void)
        {
            return Result.Fail(
                ErrorCodes.InvoiceInvalidTransition,
                $"Invoice '{Id}' is void; it has no economics left to adjust.");
        }

        _adjustments.Add(adjustment);
        RecalculateTotals();
        return Result.Ok();
    }

    /// <summary>
    /// Cancels the invoice. Legal from <see cref="InvoiceState.Draft"/> and
    /// <see cref="InvoiceState.Posted"/> and only while nothing has been paid — a paid invoice is
    /// refunded instead (<c>INVOICE_VOID_AFTER_PAYMENT</c>). Raises <see cref="InvoiceVoidedEvent"/>.
    /// </summary>
    public Result Void(string reason, IClock clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentNullException.ThrowIfNull(clock);

        if (!PaidTotal.IsZero)
        {
            return Result.Fail(
                ErrorCodes.InvoiceVoidAfterPayment,
                $"Invoice '{Id}' has {PaidTotal.Minor} paid; it must be refunded, not voided.");
        }

        var moved = Transition(InvoiceState.Void);
        if (moved.IsFailure)
        {
            return moved;
        }

        var cleanReason = reason.Trim();
        Notes = string.IsNullOrWhiteSpace(Notes) ? $"Void: {cleanReason}" : Notes;

        var now = clock.UtcNow;
        Raise(new InvoiceVoidedEvent(IdGenerator.NewId(now), TenantId, now, Id, cleanReason));
        return Result.Ok();
    }

    /// <summary>
    /// SYN-02: replaces the device-allocated (provisional) number of an invoice the server has <b>accepted</b> with the server-authoritative
    /// number. The only mutation a posted invoice permits to its number, and only for a <see cref="SettlementStatus.Settled"/> invoice —
    /// a refused/pending one keeps its provisional number until the server decides. Idempotent: the same number is a no-op.
    /// </summary>
    /// <returns><see langword="true"/> when the number changed.</returns>
    public Result<bool> AdoptServerInvoiceNumber(string serverNumber)
    {
        if (string.IsNullOrWhiteSpace(serverNumber)) return Result<bool>.Fail(ErrorCodes.ValidationFailed, "Server invoice number is empty.");
        if (State == InvoiceState.Draft || Settlement != SettlementStatus.Settled)
            return Result<bool>.Fail(ErrorCodes.ValidationFailed, $"Invoice '{Id}' is not server-settled; its number stays provisional.");
        var number = serverNumber.Trim();
        if (string.Equals(InvoiceNumber, number, StringComparison.Ordinal)) return Result<bool>.Ok(false);
        InvoiceNumber = number;
        return Result<bool>.Ok(true);
    }

    /// <summary>
    /// Records where this invoice's command stands with the server, with the error code that
    /// explains a rejection or conflict. Always surfaced to the user, never hidden
    /// (ARCHITECTURE.md §8.3).
    /// </summary>
    public Result MarkSettlement(SettlementStatus settlement, string? errorCode = null)
    {
        Settlement = settlement;
        SettlementErrorCode = Clean(errorCode);
        return Result.Ok();
    }

    /// <summary>
    /// Recomputes every total from the lines, the discount, the insurance, the payments and the
    /// adjustments via <see cref="InvoiceTotals"/>. The <b>only</b> writer of the totals.
    /// </summary>
    public void RecalculateTotals()
    {
        var totals = InvoiceTotals.Compute(_lines, DiscountKind, DiscountValue, InsurancePercent, Currency);

        Subtotal = totals.Subtotal;
        DiscountTotal = totals.DiscountTotal;
        TaxTotal = totals.TaxTotal;
        InsuranceCoverage = totals.InsuranceCoverage;
        GrandTotal = totals.GrandTotal;
        DueTotal = InvoiceTotals.ComputeDue(GrandTotal, PaidTotal, _adjustments);
    }

    /// <summary><see langword="true"/> when the tabled machine allows <paramref name="from"/> → <paramref name="to"/>.</summary>
    public static bool IsTransitionAllowed(InvoiceState from, InvoiceState to) => (from, to) switch
    {
        (InvoiceState.Draft, InvoiceState.Posted) => true,
        (InvoiceState.Draft, InvoiceState.Void) => true,
        (InvoiceState.Posted, InvoiceState.PartiallyPaid) => true,
        (InvoiceState.Posted, InvoiceState.Paid) => true,
        (InvoiceState.Posted, InvoiceState.Void) => true,
        (InvoiceState.PartiallyPaid, InvoiceState.Paid) => true,
        (InvoiceState.PartiallyPaid, InvoiceState.Refunded) => true,
        (InvoiceState.Paid, InvoiceState.Refunded) => true,
        _ => false,
    };

    /// <summary>The single gate every state change goes through.</summary>
    private Result Transition(InvoiceState target)
    {
        if (!IsTransitionAllowed(State, target))
        {
            return Result.Fail(
                ErrorCodes.InvoiceInvalidTransition,
                $"Invoice '{Id}' cannot move {State} -> {target}.");
        }

        State = target;
        return Result.Ok();
    }

    /// <summary>
    /// The single immutability guard every draft-only mutator calls — the enforcement point of
    /// A-RULE 002.
    /// </summary>
    private Result EnsureDraft() =>
        IsImmutable
            ? Result.Fail(
                ErrorCodes.InvoiceImmutable,
                $"Invoice '{Id}' is {State}; a posted invoice changes only through an adjustment.")
            : Result.Ok();

    /// <summary>Verifies the stored totals still equal a recomputation — the <c>INVOICE_TOTAL_MISMATCH</c> guard.</summary>
    private Result EnsureTotalsMatch()
    {
        var storedSubtotal = Subtotal;
        var storedDiscount = DiscountTotal;
        var storedTax = TaxTotal;
        var storedCoverage = InsuranceCoverage;
        var storedGrand = GrandTotal;
        var storedDue = DueTotal;

        RecalculateTotals();

        if (storedSubtotal != Subtotal
            || storedDiscount != DiscountTotal
            || storedTax != TaxTotal
            || storedCoverage != InsuranceCoverage
            || storedGrand != GrandTotal
            || storedDue != DueTotal)
        {
            return Result.Fail(
                ErrorCodes.InvoiceTotalMismatch,
                $"Invoice '{Id}' totals {storedGrand.Minor} disagree with the recomputed {GrandTotal.Minor}.");
        }

        return Result.Ok();
    }

    private int IndexOf(string lineId) =>
        _lines.FindIndex(l => string.Equals(l.LineId, lineId, StringComparison.Ordinal));

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
