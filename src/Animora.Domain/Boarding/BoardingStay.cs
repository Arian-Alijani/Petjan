using Animora.Domain.Events;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Domain.ValueObjects;

namespace Animora.Domain.Boarding;

/// <summary>Lifecycle state of a boarding stay.</summary>
public enum StayState
{
    /// <summary>Booked ahead; the pet has not arrived.</summary>
    Reserved = 0,

    /// <summary>The pet is boarded.</summary>
    Active = 1,

    /// <summary>Checkout requested; the billable lines are final and Sales has been handed the stay.</summary>
    ReadyForInvoice = 2,

    /// <summary>An invoice was issued for this stay.</summary>
    Invoiced = 3,

    /// <summary>The pet has left. Terminal.</summary>
    CheckedOut = 4,

    /// <summary>Abandoned before checkout. Terminal.</summary>
    Cancelled = 5,
}

/// <summary>How the stay is settled: before the stay starts or at hand-over.</summary>
public enum StayPaymentMode
{
    /// <summary>Settled at hand-over (default; persisted value 0 so existing rows keep their meaning).</summary>
    Postpaid = 0,

    /// <summary>Settled before the stay.</summary>
    Prepaid = 1,
}

/// <summary>
/// The boarding-stay aggregate root: one pet's stay in one clinic, from reservation to a billed
/// checkout.
/// </summary>
/// <remarks>
/// <para>
/// The state machine is the only authority over what may change and when (CONVENTIONS.md §6 item 9):
/// </para>
/// <code>
/// Reserved        -> Active | Cancelled
/// Active          -> ReadyForInvoice | Cancelled
/// ReadyForInvoice -> Invoiced
/// Invoiced        -> CheckedOut
/// CheckedOut / Cancelled -> (terminal)
/// </code>
/// <para>
/// The stay never computes totals. Its billable payload is <see cref="Items"/> plus
/// <c>DailyRate × BillableDays</c>; Sales (A07) turns that into an invoice. Items freeze at
/// <see cref="StayState.Invoiced"/> and above (<c>STAY_LOCKED</c>), and a stay is <b>never</b>
/// checked out unbilled — <see cref="CompleteCheckout"/> requires an attached invoice
/// (<c>STAY_INVOICE_REQUIRED</c>). Every mutator returns <see cref="Result"/>.
/// </para>
/// </remarks>
public sealed class BoardingStay : AggregateRoot
{
    private const double HoursPerDay = 24.0;

    private readonly List<StayItem> _items = [];
    private readonly List<StayRoomAssignment> _roomHistory = [];

    /// <summary>EF Core materialization constructor. Not for business code.</summary>
    private BoardingStay()
    {
    }

    /// <summary>The boarded patient.</summary>
    public string PetId { get; private set; } = string.Empty;

    /// <summary>The patient's owner, denormalized so billing never needs a second read.</summary>
    public string OwnerId { get; private set; } = string.Empty;

    /// <summary>The room the pet currently occupies.</summary>
    public string RoomId { get; private set; } = string.Empty;

    /// <summary>When the stay begins, UTC. Billable days accrue from here.</summary>
    public DateTimeOffset CheckInUtc { get; private set; }

    /// <summary>The planned check-out, UTC. Must be strictly after <see cref="CheckInUtc"/>.</summary>
    public DateTimeOffset PlannedCheckOutUtc { get; private set; }

    /// <summary>When the pet actually left, UTC; <see langword="null"/> until checkout completes.</summary>
    public DateTimeOffset? ActualCheckOutUtc { get; private set; }

    /// <summary>Current lifecycle state.</summary>
    public StayState State { get; private set; } = StayState.Reserved;

    /// <summary>The nightly room rate; multiplied by <see cref="BillableDays"/> for the room charge.</summary>
    public Money DailyRate { get; private set; }

    /// <summary>How the clinic collects payment for this stay (chosen at admission).</summary>
    public StayPaymentMode PaymentMode { get; private set; } = StayPaymentMode.Postpaid;

    /// <summary>Care instructions for staff.</summary>
    public string? CareNote { get; private set; }

    /// <summary>Diet instructions for staff.</summary>
    public string? DietNote { get; private set; }

    /// <summary>The invoice raised for this stay (A07); set by <see cref="AttachInvoice"/>.</summary>
    public string? InvoiceId { get; private set; }

    /// <summary>
    /// The prepaid room invoice issued at admission for a <see cref="StayPaymentMode.Prepaid"/> stay.
    /// Unlike <see cref="InvoiceId"/>, this never drives the lifecycle: the stay stays
    /// <see cref="StayState.Active"/> and editable so staff can still add consumed items, which are
    /// billed on a <b>second</b> invoice at checkout. <see langword="null"/> until the prepaid
    /// invoice is posted (<see cref="SettlePrepaidRoom"/>).
    /// </summary>
    public string? PrepaidInvoiceId { get; private set; }

    /// <summary>
    /// The room amount already collected on the prepaid invoice, in minor units. Excluded from the
    /// checkout bill so the room is never charged twice. <see langword="null"/> until the prepaid
    /// invoice is posted.
    /// </summary>
    public long? PrepaidRoomChargeMinor { get; private set; }

    /// <summary><see langword="true"/> once the room charge has been collected in advance.</summary>
    public bool IsRoomPrepaid => PrepaidInvoiceId is not null;

    /// <summary>Auditable checkout snapshot, not the source of the running balance.</summary>
    public long? FrozenTotalMinor { get; private set; }

    /// <summary>Records the derived total once, when the stay is handed to billing.</summary>
    public Result FreezeTotalForCheckout(Money total)
    {
        if (State != StayState.ReadyForInvoice || FrozenTotalMinor is not null)
        {
            return Result.Fail(ErrorCodes.StayNotEditable);
        }

        if (total.Currency != DailyRate.Currency || total.IsNegative)
        {
            return Result.Fail(ErrorCodes.ValidationFailed);
        }

        FrozenTotalMinor = total.Minor;
        return Result.Ok();
    }


    /// <summary>Consumed items — part of the payload Sales converts into invoice lines.</summary>
    public IReadOnlyList<StayItem> Items => _items;

    /// <summary>The full room timeline of this stay, oldest first.</summary>
    public IReadOnlyList<StayRoomAssignment> RoomHistory => _roomHistory;

    /// <summary><see langword="true"/> once the items may no longer change.</summary>
    public bool AreItemsLocked => State >= StayState.Invoiced;

    /// <summary>
    /// Reserves a stay in <see cref="StayState.Reserved"/>. The planned check-out must be strictly
    /// after the check-in (<c>STAY_DATES_INVALID</c>).
    /// </summary>
    public static Result<BoardingStay> Reserve(
        string tenantId,
        string petId,
        string ownerId,
        string roomId,
        DateTimeOffset checkInUtc,
        DateTimeOffset plannedCheckOutUtc,
        Money dailyRate,
        string? careNote = null,
        string? dietNote = null,
        StayPaymentMode paymentMode = StayPaymentMode.Postpaid,
        string? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(petId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(roomId);

        if (plannedCheckOutUtc <= checkInUtc)
        {
            return Result<BoardingStay>.Fail(
                ErrorCodes.StayDatesInvalid,
                $"Planned check-out {plannedCheckOutUtc:o} must be after check-in {checkInUtc:o}.");
        }

        return new BoardingStay
        {
            Id = string.IsNullOrWhiteSpace(id) ? IdGenerator.NewId(checkInUtc) : id,
            TenantId = tenantId,
            PetId = petId,
            OwnerId = ownerId,
            RoomId = roomId,
            CheckInUtc = checkInUtc,
            PlannedCheckOutUtc = plannedCheckOutUtc,
            State = StayState.Reserved,
            DailyRate = dailyRate,
            PaymentMode = paymentMode,
            CareNote = Clean(careNote),
            DietNote = Clean(dietNote),
        };
    }

    /// <summary>
    /// Admits the pet, opening the first room-history entry. Raises <see cref="StayActivatedEvent"/>.
    /// </summary>
    public Result Activate(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        var moved = Transition(StayState.Active);
        if (moved.IsFailure)
        {
            return moved;
        }

        var now = clock.UtcNow;
        _roomHistory.Add(new StayRoomAssignment(RoomId, CheckInUtc, ToUtc: null, Reason: null));
        Raise(new StayActivatedEvent(IdGenerator.NewId(now), TenantId, now, Id, PetId, OwnerId, RoomId));
        return Result.Ok();
    }

    /// <summary>Adds a consumed item. Refused once the stay is invoiced (<c>STAY_LOCKED</c>).</summary>
    public Result AddItem(StayItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var guard = EnsureItemsEditable();
        if (guard.IsFailure)
        {
            return guard;
        }

        _items.Add(item);
        return Result.Ok();
    }

    /// <summary>Removes a consumed item by its id. Refused once the stay is invoiced.</summary>
    public Result RemoveItem(string itemId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        var guard = EnsureItemsEditable();
        if (guard.IsFailure)
        {
            return guard;
        }

        var index = _items.FindIndex(i => string.Equals(i.ItemId, itemId, StringComparison.Ordinal));
        if (index < 0)
        {
            return Result.Fail(ErrorCodes.NotFound, $"Item '{itemId}' is not on stay '{Id}'.");
        }

        _items.RemoveAt(index);
        return Result.Ok();
    }

    /// <summary>
    /// Transfers the pet to a new room. Legal only while <see cref="StayState.Active"/>: it closes the
    /// current room-history entry and appends a new open one. Raises <see cref="StayRoomTransferredEvent"/>.
    /// </summary>
    public Result TransferRoom(string newRoomId, string reason, IClock clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newRoomId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentNullException.ThrowIfNull(clock);

        if (State != StayState.Active)
        {
            return Result.Fail(
                ErrorCodes.StayInvalidTransition,
                $"Stay '{Id}' is {State}; a room transfer is only allowed while active.");
        }

        var now = clock.UtcNow;
        var fromRoomId = RoomId;
        var cleanReason = reason.Trim();

        var openIndex = _roomHistory.FindIndex(a => a.ToUtc is null);
        if (openIndex >= 0)
        {
            _roomHistory[openIndex] = _roomHistory[openIndex] with { ToUtc = now };
        }

        _roomHistory.Add(new StayRoomAssignment(newRoomId, now, ToUtc: null, cleanReason));
        RoomId = newRoomId;

        Raise(new StayRoomTransferredEvent(
            IdGenerator.NewId(now), TenantId, now, Id, fromRoomId, newRoomId, cleanReason));
        return Result.Ok();
    }

    /// <summary>
    /// Requests checkout and hands the stay to Sales. Freezes the billable lines: <see cref="Items"/>
    /// plus <c>DailyRate × BillableDays(clock.UtcNow)</c> are the invoice payload — the stay never
    /// computes totals itself. Raises <see cref="StayReadyForInvoiceEvent"/>.
    /// </summary>
    public Result RequestCheckout(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        var moved = Transition(StayState.ReadyForInvoice);
        if (moved.IsFailure)
        {
            return moved;
        }

        var now = clock.UtcNow;
        Raise(new StayReadyForInvoiceEvent(
            IdGenerator.NewId(now), TenantId, now, Id, PetId, OwnerId, BillableDays(now)));
        return Result.Ok();
    }

    /// <summary>
    /// Links the issued invoice and moves to <see cref="StayState.Invoiced"/>. Only legal from
    /// <see cref="StayState.ReadyForInvoice"/>.
    /// </summary>
    public Result AttachInvoice(string invoiceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(invoiceId);

        var moved = Transition(StayState.Invoiced);
        if (moved.IsFailure)
        {
            return moved;
        }

        InvoiceId = invoiceId;
        return Result.Ok();
    }

    /// <summary>
    /// Records the posted prepaid room invoice for a <see cref="StayPaymentMode.Prepaid"/> stay.
    /// This is <b>not</b> a lifecycle transition: the stay stays <see cref="StayState.Active"/> and its
    /// items stay editable, so staff can keep adding consumed items that are billed separately at
    /// checkout. Rejected if the stay is not prepaid (<c>STAY_NOT_EDITABLE</c>), already settled, or no
    /// longer active.
    /// </summary>
    public Result SettlePrepaidRoom(string invoiceId, Money roomCharge)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(invoiceId);

        if (PaymentMode != StayPaymentMode.Prepaid || PrepaidInvoiceId is not null ||
            State != StayState.Active)
        {
            return Result.Fail(ErrorCodes.StayNotEditable,
                $"Stay '{Id}' ({State}, {PaymentMode}) cannot record a prepaid room invoice.");
        }

        if (roomCharge.Currency != DailyRate.Currency || roomCharge.IsNegative)
        {
            return Result.Fail(ErrorCodes.ValidationFailed);
        }

        PrepaidInvoiceId = invoiceId;
        PrepaidRoomChargeMinor = roomCharge.Minor;
        return Result.Ok();
    }

    /// <summary>
    /// Completes checkout, stamping <see cref="ActualCheckOutUtc"/> and closing the room history. A
    /// stay is <b>never</b> checked out unbilled — an invoice must be attached first
    /// (<c>STAY_INVOICE_REQUIRED</c>). Raises <see cref="StayCheckedOutEvent"/>.
    /// </summary>
    public Result CompleteCheckout(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (string.IsNullOrWhiteSpace(InvoiceId))
        {
            return Result.Fail(
                ErrorCodes.StayInvoiceRequired,
                $"Stay '{Id}' cannot be checked out without an invoice.");
        }

        var moved = Transition(StayState.CheckedOut);
        if (moved.IsFailure)
        {
            return moved;
        }

        var now = clock.UtcNow;
        ActualCheckOutUtc = now;

        var openIndex = _roomHistory.FindIndex(a => a.ToUtc is null);
        if (openIndex >= 0)
        {
            _roomHistory[openIndex] = _roomHistory[openIndex] with { ToUtc = now };
        }

        Raise(new StayCheckedOutEvent(IdGenerator.NewId(now), TenantId, now, Id, PetId, RoomId, now));
        return Result.Ok();
    }

    /// <summary>
    /// Checks out a fully-prepaid stay that has no extra consumed items to bill: the room was already
    /// collected in advance (<see cref="IsRoomPrepaid"/>) and <see cref="Items"/> is empty, so there is
    /// nothing left to invoice. Moves <see cref="StayState.Active"/> straight to
    /// <see cref="StayState.CheckedOut"/> without a second invoice. Rejected otherwise
    /// (<c>STAY_INVOICE_REQUIRED</c>) so a stay with unbilled items still goes through Sales.
    /// </summary>
    public Result CompletePrepaidCheckoutWithoutInvoice(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (State != StayState.Active || !IsRoomPrepaid || _items.Count != 0)
        {
            return Result.Fail(
                ErrorCodes.StayInvoiceRequired,
                $"Stay '{Id}' still has billable lines; it must be invoiced before checkout.");
        }

        var now = clock.UtcNow;
        State = StayState.CheckedOut;
        ActualCheckOutUtc = now;

        var openIndex = _roomHistory.FindIndex(a => a.ToUtc is null);
        if (openIndex >= 0)
        {
            _roomHistory[openIndex] = _roomHistory[openIndex] with { ToUtc = now };
        }

        Raise(new StayCheckedOutEvent(IdGenerator.NewId(now), TenantId, now, Id, PetId, RoomId, now));
        return Result.Ok();
    }

    /// <summary>Abandons the stay. Legal only before it is handed to Sales.</summary>
    public Result Cancel(string reason, IClock clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentNullException.ThrowIfNull(clock);

        var moved = Transition(StayState.Cancelled);
        if (moved.IsFailure)
        {
            return moved;
        }

        CareNote = string.IsNullOrWhiteSpace(CareNote)
            ? $"Cancelled: {reason.Trim()}"
            : CareNote;
        return Result.Ok();
    }

    /// <summary>
    /// Moves the planned check-out (extends or shortens the stay). Legal only while the stay is
    /// <see cref="StayState.Active"/> and the new instant is strictly after <see cref="CheckInUtc"/>
    /// (<c>STAY_DATES_INVALID</c>). A pure schedule change: it never touches <see cref="ActualCheckOutUtc"/>
    /// and does not bill — nights are billed from the real elapsed days at checkout.
    /// </summary>
    public Result RescheduleCheckout(DateTimeOffset newPlannedCheckOutUtc)
    {
        if (State != StayState.Active)
        {
            return Result.Fail(ErrorCodes.StayNotEditable,
                $"Stay '{Id}' ({State}) cannot be rescheduled.");
        }

        if (newPlannedCheckOutUtc <= CheckInUtc)
        {
            return Result.Fail(ErrorCodes.StayDatesInvalid,
                $"Planned check-out {newPlannedCheckOutUtc:o} must be after check-in {CheckInUtc:o}.");
        }

        PlannedCheckOutUtc = newPlannedCheckOutUtc;
        return Result.Ok();
    }

    /// <summary>Updates the care and diet instructions.</summary>
    public Result SetNotes(string? careNote, string? dietNote)
    {
        CareNote = Clean(careNote);
        DietNote = Clean(dietNote);
        return Result.Ok();
    }

    /// <summary>
    /// The <b>authoritative</b> billable-days rule:
    /// <c>Math.Max(1, ceil((endUtc - CheckInUtc).TotalHours / 24))</c>. A partial day counts as a
    /// full day and a same-day stay counts as one: 1h → 1, 24h → 1, 25h → 2.
    /// </summary>
    /// <param name="endUtc">The instant checkout is measured to (typically <c>now</c> at checkout).</param>
    public int BillableDays(DateTimeOffset endUtc)
    {
        var hours = (endUtc - CheckInUtc).TotalHours;
        var days = (int)Math.Ceiling(hours / HoursPerDay);
        return Math.Max(1, days);
    }

    /// <summary><see langword="true"/> when the tabled machine allows <paramref name="from"/> → <paramref name="to"/>.</summary>
    public static bool IsTransitionAllowed(StayState from, StayState to) => (from, to) switch
    {
        (StayState.Reserved, StayState.Active) => true,
        (StayState.Reserved, StayState.Cancelled) => true,
        (StayState.Active, StayState.ReadyForInvoice) => true,
        (StayState.Active, StayState.Cancelled) => true,
        (StayState.ReadyForInvoice, StayState.Invoiced) => true,
        (StayState.Invoiced, StayState.CheckedOut) => true,
        _ => false,
    };

    /// <summary>The single gate every state change goes through.</summary>
    private Result Transition(StayState target)
    {
        if (!IsTransitionAllowed(State, target))
        {
            return Result.Fail(
                ErrorCodes.StayInvalidTransition,
                $"Stay '{Id}' cannot move {State} -> {target}.");
        }

        State = target;
        return Result.Ok();
    }

    private Result EnsureItemsEditable() =>
        AreItemsLocked
            ? Result.Fail(ErrorCodes.StayLocked, $"Stay '{Id}' is {State}; its items are final.")
            : Result.Ok();

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
