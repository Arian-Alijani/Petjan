using Animora.Domain.Events;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Domain.ValueObjects;

namespace Animora.Domain.Purchasing;

/// <summary>Lifecycle state of a purchase order.</summary>
public enum PurchaseOrderState
{
    /// <summary>Being composed; lines may still change.</summary>
    Draft = 0,

    /// <summary>Sent to the supplier; lines are frozen.</summary>
    Submitted = 1,

    /// <summary>Some, but not all, ordered quantity has arrived.</summary>
    PartiallyReceived = 2,

    /// <summary>Every line has arrived in full.</summary>
    Received = 3,

    /// <summary>Abandoned before delivery. Terminal.</summary>
    Cancelled = 4,

    /// <summary>Finished and filed; no further receipt is expected. Terminal.</summary>
    Closed = 5,
}

/// <summary>
/// The purchase-order aggregate root: what the clinic ordered from one supplier, at what prices,
/// and how much of it has arrived.
/// </summary>
/// <remarks>
/// <para>The state machine is the only authority over what may change and when:</para>
/// <code>
/// Draft             -> Submitted | Cancelled
/// Submitted         -> PartiallyReceived | Received | Cancelled
/// PartiallyReceived -> Received | Closed
/// Received          -> Closed
/// Cancelled / Closed -> (terminal)
/// </code>
/// <para>
/// Lines are immutable from <see cref="PurchaseOrderState.Submitted"/> on (<c>PO_LOCKED</c>).
/// Totals are never set from outside: <see cref="RecalculateTotals"/> runs after every line change
/// and the order refuses to be submitted when the stored totals disagree with the line sum
/// (<c>PO_TOTAL_MISMATCH</c>). All money arithmetic goes through
/// <see cref="Money.ApplyPercent(decimal)"/> — the product's single rounding boundary.
/// </para>
/// <para>
/// Receiving never writes stock here: <see cref="ApplyReceipt"/> only advances the per-line
/// received quantity and the order state. The ledger is written by the movement writer (D07/C06)
/// from <see cref="GoodsReceipt"/>'s posted event.
/// </para>
/// </remarks>
public sealed class PurchaseOrder : AggregateRoot
{
    private readonly List<PurchaseOrderLine> _lines = [];

    /// <summary>EF Core materialization constructor. Not for business code.</summary>
    private PurchaseOrder()
    {
    }

    /// <summary>Per-tenant sequential order number, assigned by the Application layer.</summary>
    public string OrderNumber { get; private set; } = string.Empty;

    /// <summary>The supplier the order is placed with.</summary>
    public string SupplierId { get; private set; } = string.Empty;

    /// <summary>Current lifecycle state.</summary>
    public PurchaseOrderState State { get; private set; } = PurchaseOrderState.Draft;

    /// <summary>When the order was raised, UTC.</summary>
    public DateTimeOffset OrderedUtc { get; private set; }

    /// <summary>Expected delivery date, UTC; <see langword="null"/> when the supplier gave none.</summary>
    public DateTimeOffset? ExpectedUtc { get; private set; }

    /// <summary>Sum of the lines' gross amounts, before discount and tax.</summary>
    public Money Subtotal { get; private set; }

    /// <summary>Sum of the lines' discount amounts.</summary>
    public Money DiscountTotal { get; private set; }

    /// <summary>Sum of the lines' tax amounts.</summary>
    public Money TaxTotal { get; private set; }

    /// <summary><c>Subtotal - DiscountTotal + TaxTotal</c>: what the order costs.</summary>
    public Money GrandTotal { get; private set; }

    /// <summary>The staff member who raised the order.</summary>
    public string CreatedByUserId { get; private set; } = string.Empty;

    /// <summary>Free-form notes.</summary>
    public string? Notes { get; private set; }

    /// <summary>The ordered lines; frozen from <see cref="PurchaseOrderState.Submitted"/> on.</summary>
    public IReadOnlyList<PurchaseOrderLine> Lines => _lines;

    /// <summary><see langword="true"/> once the lines may no longer change.</summary>
    public bool AreLinesLocked => State != PurchaseOrderState.Draft;

    /// <summary>Currency every amount on this order is expressed in.</summary>
    public string Currency => GrandTotal.Currency;

    /// <summary>Creates an empty order in <see cref="PurchaseOrderState.Draft"/>.</summary>
    /// <param name="tenantId">Owning tenant.</param>
    /// <param name="orderNumber">Per-tenant sequential number, assigned by the Application layer.</param>
    /// <param name="supplierId">The supplier ordered from.</param>
    /// <param name="orderedUtc">When the order is raised, UTC.</param>
    /// <param name="createdByUserId">The staff member raising it.</param>
    /// <param name="currency">ISO-4217 code every amount on the order uses.</param>
    /// <param name="expectedUtc">Expected delivery, UTC.</param>
    /// <param name="notes">Free-form notes.</param>
    public static Result<PurchaseOrder> Create(
        string tenantId,
        string orderNumber,
        string supplierId,
        DateTimeOffset orderedUtc,
        string createdByUserId,
        string currency = CurrencyScale.Irt,
        DateTimeOffset? expectedUtc = null,
        string? notes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(orderNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(supplierId);
        ArgumentException.ThrowIfNullOrWhiteSpace(createdByUserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);

        if (expectedUtc is not null && expectedUtc < orderedUtc)
        {
            return Result<PurchaseOrder>.Fail(
                ErrorCodes.ValidationFailed,
                $"Expected date {expectedUtc:o} precedes the order date {orderedUtc:o}.");
        }

        var zero = Money.Zero(currency);
        return new PurchaseOrder
        {
            Id = IdGenerator.NewId(orderedUtc),
            TenantId = tenantId,
            OrderNumber = orderNumber.Trim(),
            SupplierId = supplierId,
            State = PurchaseOrderState.Draft,
            OrderedUtc = orderedUtc,
            ExpectedUtc = expectedUtc,
            Subtotal = zero,
            DiscountTotal = zero,
            TaxTotal = zero,
            GrandTotal = zero,
            CreatedByUserId = createdByUserId,
            Notes = Clean(notes),
        };
    }

    /// <summary>
    /// Appends a line and recomputes the totals. Refused once the order is submitted
    /// (<c>PO_LOCKED</c>); the line's quantity must be positive and its currency must match the
    /// order's.
    /// </summary>
    public Result AddLine(PurchaseOrderLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        var guard = EnsureLinesEditable();
        if (guard.IsFailure)
        {
            return guard;
        }

        if (line.OrderedQuantity.Value <= 0m)
        {
            return Result.Fail(
                ErrorCodes.ValidationFailed,
                $"Line '{line.LineId}' must order a positive quantity; got {line.OrderedQuantity.Value}.");
        }

        if (!string.Equals(line.UnitCost.Currency, Currency, StringComparison.Ordinal))
        {
            return Result.Fail(
                ErrorCodes.ValidationFailed,
                $"Line currency '{line.UnitCost.Currency}' does not match order currency '{Currency}'.");
        }

        if (!string.Equals(line.OrderedQuantity.UnitCode, line.ReceivedQuantity.UnitCode, StringComparison.Ordinal))
        {
            return Result.Fail(
                ErrorCodes.UnitMismatch,
                $"Line '{line.LineId}' mixes units '{line.OrderedQuantity.UnitCode}' and '{line.ReceivedQuantity.UnitCode}'.");
        }

        _lines.Add(line);
        RecalculateTotals();
        return Result.Ok();
    }

    /// <summary>Removes a line by its id and recomputes the totals. Refused once submitted.</summary>
    public Result RemoveLine(string lineId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lineId);

        var guard = EnsureLinesEditable();
        if (guard.IsFailure)
        {
            return guard;
        }

        var index = IndexOf(lineId);
        if (index < 0)
        {
            return Result.Fail(ErrorCodes.NotFound, $"Line '{lineId}' is not on order '{Id}'.");
        }

        _lines.RemoveAt(index);
        RecalculateTotals();
        return Result.Ok();
    }

    /// <summary>
    /// Sends the order to the supplier: freezes the lines and raises
    /// <see cref="PurchaseOrderSubmittedEvent"/>. Refused for an empty order and when the stored
    /// totals disagree with the line sum (<c>PO_TOTAL_MISMATCH</c>).
    /// </summary>
    public Result Submit(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (_lines.Count == 0)
        {
            return Result.Fail(ErrorCodes.ValidationFailed, $"Order '{Id}' has no lines to submit.");
        }

        var totals = EnsureTotalsMatch();
        if (totals.IsFailure)
        {
            return totals;
        }

        var moved = Transition(PurchaseOrderState.Submitted);
        if (moved.IsFailure)
        {
            return moved;
        }

        var now = clock.UtcNow;
        Raise(new PurchaseOrderSubmittedEvent(
            IdGenerator.NewId(now), TenantId, now, Id, SupplierId, GrandTotal.Minor, GrandTotal.Currency));
        return Result.Ok();
    }

    /// <summary>
    /// Records what a posted <paramref name="receipt"/> delivered against this order: adds each
    /// receipt line's quantity to the matching order line and moves the order to
    /// <see cref="PurchaseOrderState.PartiallyReceived"/> or <see cref="PurchaseOrderState.Received"/>
    /// by comparing received against ordered quantity per line.
    /// </summary>
    /// <remarks>
    /// Stock is <b>not</b> written here — the ledger is written by the movement writer from the
    /// receipt's posted event. A receipt line that does not match any order line is ignored: a
    /// direct receipt may legitimately carry extra goods, and the ledger still records them.
    /// </remarks>
    public Result ApplyReceipt(GoodsReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        if (State is not (PurchaseOrderState.Submitted or PurchaseOrderState.PartiallyReceived))
        {
            return Result.Fail(
                ErrorCodes.PurchaseOrderInvalidTransition,
                $"Order '{Id}' is {State}; a receipt can only be applied while submitted or partially received.");
        }

        var lines = receipt.Lines;
        for (var i = 0; i < lines.Count; i++)
        {
            var received = lines[i];
            var index = IndexOfVariant(received.ProductId, received.VariantId);
            if (index < 0)
            {
                continue;
            }

            var line = _lines[index];
            if (!string.Equals(line.OrderedQuantity.UnitCode, received.Quantity.UnitCode, StringComparison.Ordinal))
            {
                return Result.Fail(
                    ErrorCodes.UnitMismatch,
                    $"Receipt line '{received.LineId}' is in '{received.Quantity.UnitCode}'; order line '{line.LineId}' is in '{line.OrderedQuantity.UnitCode}'.");
            }

            _lines[index] = line with
            {
                ReceivedQuantity = line.ReceivedQuantity.Add(received.Quantity.Abs()),
            };
        }

        return Transition(AllLinesFullyReceived()
            ? PurchaseOrderState.Received
            : PurchaseOrderState.PartiallyReceived);
    }

    /// <summary>Abandons the order. Legal only before anything has arrived.</summary>
    public Result Cancel(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var moved = Transition(PurchaseOrderState.Cancelled);
        if (moved.IsFailure)
        {
            return moved;
        }

        Notes = string.IsNullOrWhiteSpace(Notes) ? $"Cancelled: {reason.Trim()}" : Notes;
        return Result.Ok();
    }

    /// <summary>Files the order; no further receipt is expected against it.</summary>
    public Result Close() => Transition(PurchaseOrderState.Closed);

    /// <summary>Updates the expected delivery date. Refused once the lines are frozen.</summary>
    public Result SetExpectedDate(DateTimeOffset? expectedUtc)
    {
        if (expectedUtc is not null && expectedUtc < OrderedUtc)
        {
            return Result.Fail(
                ErrorCodes.ValidationFailed,
                $"Expected date {expectedUtc:o} precedes the order date {OrderedUtc:o}.");
        }

        ExpectedUtc = expectedUtc;
        return Result.Ok();
    }

    /// <summary>
    /// Recomputes <see cref="Subtotal"/>, <see cref="DiscountTotal"/>, <see cref="TaxTotal"/> and
    /// <see cref="GrandTotal"/> from the lines. The <b>only</b> writer of the totals.
    /// </summary>
    public void RecalculateTotals()
    {
        var subtotal = 0L;
        var discount = 0L;
        var tax = 0L;

        for (var i = 0; i < _lines.Count; i++)
        {
            var line = _lines[i];
            subtotal += line.Gross.Minor;
            discount += line.DiscountAmount.Minor;
            tax += line.TaxAmount.Minor;
        }

        var currency = Currency;
        Subtotal = new Money(subtotal, currency);
        DiscountTotal = new Money(discount, currency);
        TaxTotal = new Money(tax, currency);
        GrandTotal = new Money(subtotal - discount + tax, currency);
    }

    /// <summary><see langword="true"/> when the tabled machine allows <paramref name="from"/> → <paramref name="to"/>.</summary>
    public static bool IsTransitionAllowed(PurchaseOrderState from, PurchaseOrderState to) => (from, to) switch
    {
        (PurchaseOrderState.Draft, PurchaseOrderState.Submitted) => true,
        (PurchaseOrderState.Draft, PurchaseOrderState.Cancelled) => true,
        (PurchaseOrderState.Submitted, PurchaseOrderState.PartiallyReceived) => true,
        (PurchaseOrderState.Submitted, PurchaseOrderState.Received) => true,
        (PurchaseOrderState.Submitted, PurchaseOrderState.Cancelled) => true,
        (PurchaseOrderState.PartiallyReceived, PurchaseOrderState.PartiallyReceived) => true,
        (PurchaseOrderState.PartiallyReceived, PurchaseOrderState.Received) => true,
        (PurchaseOrderState.PartiallyReceived, PurchaseOrderState.Closed) => true,
        (PurchaseOrderState.Received, PurchaseOrderState.Closed) => true,
        _ => false,
    };

    /// <summary>
    /// Verifies the stored totals still equal the line sum — the guard behind
    /// <c>PO_TOTAL_MISMATCH</c>.
    /// </summary>
    private Result EnsureTotalsMatch()
    {
        var storedGrand = GrandTotal;
        var storedSubtotal = Subtotal;
        var storedDiscount = DiscountTotal;
        var storedTax = TaxTotal;

        RecalculateTotals();

        if (storedGrand != GrandTotal
            || storedSubtotal != Subtotal
            || storedDiscount != DiscountTotal
            || storedTax != TaxTotal)
        {
            return Result.Fail(
                ErrorCodes.PurchaseOrderTotalMismatch,
                $"Order '{Id}' totals {storedGrand.Minor} disagree with the line sum {GrandTotal.Minor}.");
        }

        return Result.Ok();
    }

    /// <summary>The single gate every state change goes through.</summary>
    private Result Transition(PurchaseOrderState target)
    {
        if (!IsTransitionAllowed(State, target))
        {
            return Result.Fail(
                ErrorCodes.PurchaseOrderInvalidTransition,
                $"Order '{Id}' cannot move {State} -> {target}.");
        }

        State = target;
        return Result.Ok();
    }

    private Result EnsureLinesEditable() =>
        AreLinesLocked
            ? Result.Fail(ErrorCodes.PurchaseOrderLocked, $"Order '{Id}' is {State}; its lines are final.")
            : Result.Ok();

    private bool AllLinesFullyReceived()
    {
        for (var i = 0; i < _lines.Count; i++)
        {
            if (!_lines[i].IsFullyReceived)
            {
                return false;
            }
        }

        return _lines.Count > 0;
    }

    private int IndexOf(string lineId)
    {
        for (var i = 0; i < _lines.Count; i++)
        {
            if (string.Equals(_lines[i].LineId, lineId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private int IndexOfVariant(string productId, string variantId)
    {
        for (var i = 0; i < _lines.Count; i++)
        {
            var line = _lines[i];
            if (string.Equals(line.VariantId, variantId, StringComparison.Ordinal)
                && string.Equals(line.ProductId, productId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
