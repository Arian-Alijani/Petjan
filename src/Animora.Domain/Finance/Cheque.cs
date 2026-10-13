using Animora.Domain.Events;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Domain.ValueObjects;

namespace Animora.Domain.Finance;

/// <summary>Which way a cheque moves.</summary>
public enum ChequeDirection
{
    /// <summary>Received from a customer: an asset until it clears.</summary>
    Inbound = 0,

    /// <summary>Written to a supplier: a liability until it clears.</summary>
    Outbound = 1,
}

/// <summary>Lifecycle state of a cheque.</summary>
public enum ChequeState
{
    /// <summary>Recorded in the system; not yet filed in the portfolio.</summary>
    Registered = 0,

    /// <summary>Held in the clinic's cheque portfolio, waiting for its due date.</summary>
    InPortfolio = 1,

    /// <summary>Handed to the bank for collection / payment.</summary>
    Deposited = 2,

    /// <summary>The bank honoured it. Terminal.</summary>
    Cleared = 3,

    /// <summary>The bank refused it; the receivable comes back.</summary>
    Bounced = 4,

    /// <summary>Handed back to the counterparty. Terminal.</summary>
    Returned = 5,

    /// <summary>Voided before it ever went to the bank. Terminal.</summary>
    Cancelled = 6,

    /// <summary>A bounced cheque was settled in cash instead. Terminal.</summary>
    Settled = 7,
}

/// <summary>
/// A cheque — inbound (received from an owner) or outbound (written to a supplier) — with its
/// full lifecycle and an append-only transition history.
/// </summary>
/// <remarks>
/// <para><b>State machine:</b></para>
/// <code>
/// Registered  -> InPortfolio | Cancelled
/// InPortfolio -> Deposited | Returned | Cancelled
/// Deposited   -> Cleared | Bounced
/// Bounced     -> InPortfolio | Returned | Settled
/// Cleared / Returned / Cancelled / Settled -> terminal
/// </code>
/// <para>
/// Every accepted transition appends exactly one <see cref="ChequeEvent"/> to
/// <see cref="History"/> and raises <see cref="ChequeStateChangedEvent"/>; clearing and bouncing
/// additionally raise the events the ledger listens to
/// (<see cref="ChequeClearedEvent"/> / <see cref="ChequeBouncedEvent"/>, translated to journal
/// entries by <see cref="PostingRules"/>). A rejected transition returns
/// <c>CHEQUE_INVALID_TRANSITION</c> and changes nothing.
/// </para>
/// <para>
/// <b>[INVARIANT] Counterparty.</b> An <see cref="ChequeDirection.Inbound"/> cheque must name the
/// owner it came from and an <see cref="ChequeDirection.Outbound"/> one the supplier it was
/// written to (<c>CHEQUE_COUNTERPARTY_REQUIRED</c>) — a cheque with no counterparty can never be
/// chased, returned or reconciled.
/// </para>
/// </remarks>
public sealed class Cheque : AggregateRoot
{
    private readonly List<ChequeEvent> _history = [];

    /// <summary>EF Core materialization constructor. Not for business code.</summary>
    private Cheque()
    {
    }

    /// <summary>The number printed on the cheque leaf.</summary>
    public string ChequeNumber { get; private set; } = string.Empty;

    /// <summary>Whether the clinic receives or pays with this cheque.</summary>
    public ChequeDirection Direction { get; private set; }

    /// <summary>Drawee bank name.</summary>
    public string? BankName { get; private set; }

    /// <summary>Drawee branch.</summary>
    public string? BankBranch { get; private set; }

    /// <summary>The drawer's account number.</summary>
    public string? AccountNumber { get; private set; }

    /// <summary>The Iranian central-bank Sayad identifier, when the leaf carries one.</summary>
    public string? SayadId { get; private set; }

    /// <summary>The face amount.</summary>
    public Money Amount { get; private set; }

    /// <summary>The date written on the cheque, UTC.</summary>
    public DateTimeOffset DueUtc { get; private set; }

    /// <summary>When the cheque was handed over / written, UTC.</summary>
    public DateTimeOffset IssuedUtc { get; private set; }

    /// <summary>Current lifecycle state — a cache of the last <see cref="History"/> entry.</summary>
    public ChequeState State { get; private set; } = ChequeState.Registered;

    /// <summary>The owner who handed it over; mandatory for an inbound cheque.</summary>
    public string? OwnerId { get; private set; }

    /// <summary>The supplier it was written to; mandatory for an outbound cheque.</summary>
    public string? SupplierId { get; private set; }

    /// <summary>The invoice it pays, when there is one.</summary>
    public string? InvoiceId { get; private set; }

    /// <summary>The <c>Payment</c> row backing it, when it settles an invoice.</summary>
    public string? PaymentId { get; private set; }

    /// <summary>The journal entry that recognised it in the ledger.</summary>
    public string? JournalEntryId { get; private set; }

    /// <summary>Free-form note, kept for the audit trail.</summary>
    public string? Note { get; private set; }

    /// <summary>The append-only transition history, oldest first.</summary>
    public IReadOnlyList<ChequeEvent> History => _history;

    /// <summary><see langword="true"/> once no further transition is possible.</summary>
    public bool IsTerminal => State is ChequeState.Cleared or ChequeState.Returned
        or ChequeState.Cancelled or ChequeState.Settled;

    /// <summary>ISO-4217 code of the face amount.</summary>
    public string Currency => Amount.Currency;

    /// <summary>
    /// Registers a cheque in <see cref="ChequeState.Registered"/>, enforcing the counterparty
    /// invariant and a strictly positive amount.
    /// </summary>
    /// <param name="tenantId">Owning tenant.</param>
    /// <param name="chequeNumber">The number on the leaf.</param>
    /// <param name="direction">Inbound (received) or outbound (written).</param>
    /// <param name="amount">The face amount; strictly positive.</param>
    /// <param name="dueUtc">The date written on the cheque, UTC.</param>
    /// <param name="issuedUtc">When it changed hands, UTC.</param>
    /// <param name="ownerId">The owner it came from; mandatory when inbound.</param>
    /// <param name="supplierId">The supplier it was written to; mandatory when outbound.</param>
    /// <param name="bankName">Drawee bank.</param>
    /// <param name="bankBranch">Drawee branch.</param>
    /// <param name="accountNumber">Drawer account number.</param>
    /// <param name="sayadId">Sayad identifier.</param>
    /// <param name="invoiceId">The invoice it pays, when there is one.</param>
    /// <param name="paymentId">The payment row backing it.</param>
    /// <param name="note">Audit-trail note.</param>
    public static Result<Cheque> Register(
        string tenantId,
        string chequeNumber,
        ChequeDirection direction,
        Money amount,
        DateTimeOffset dueUtc,
        DateTimeOffset issuedUtc,
        string? ownerId = null,
        string? supplierId = null,
        string? bankName = null,
        string? bankBranch = null,
        string? accountNumber = null,
        string? sayadId = null,
        string? invoiceId = null,
        string? paymentId = null,
        string? note = null,
        string? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(chequeNumber);

        if (amount.Minor <= 0L)
        {
            return Result<Cheque>.Fail(
                ErrorCodes.ValidationFailed,
                $"Cheque '{chequeNumber}' must carry a strictly positive amount; got {amount.Minor}.");
        }

        if (direction == ChequeDirection.Inbound && string.IsNullOrWhiteSpace(ownerId))
        {
            return Result<Cheque>.Fail(
                ErrorCodes.ChequeCounterpartyRequired,
                "An inbound cheque must name the owner it was received from.");
        }

        if (direction == ChequeDirection.Outbound && string.IsNullOrWhiteSpace(supplierId))
        {
            return Result<Cheque>.Fail(
                ErrorCodes.ChequeCounterpartyRequired,
                "An outbound cheque must name the supplier it was written to.");
        }

        return new Cheque
        {
            Id = string.IsNullOrWhiteSpace(id) ? IdGenerator.NewId(issuedUtc) : id,
            TenantId = tenantId,
            ChequeNumber = chequeNumber.Trim(),
            Direction = direction,
            Amount = amount,
            DueUtc = dueUtc,
            IssuedUtc = issuedUtc,
            State = ChequeState.Registered,
            OwnerId = Clean(ownerId),
            SupplierId = Clean(supplierId),
            BankName = Clean(bankName),
            BankBranch = Clean(bankBranch),
            AccountNumber = Clean(accountNumber),
            SayadId = Clean(sayadId),
            InvoiceId = Clean(invoiceId),
            PaymentId = Clean(paymentId),
            Note = Clean(note),
        };
    }

    /// <summary>Changes descriptive fields only while the cheque is still registered.</summary>
    public Result UpdateMetadata(string chequeNumber, DateTimeOffset dueUtc, string? bankName,
        string? bankBranch, string? accountNumber, string? sayadId, string? note)
    {
        if (State != ChequeState.Registered || Deleted)
            return Result.Fail(ErrorCodes.ChequeNotEditable);
        if (string.IsNullOrWhiteSpace(chequeNumber))
            return Result.Fail(ErrorCodes.ValidationFailed, "Cheque number is required.");
        ChequeNumber = chequeNumber.Trim();
        DueUtc = dueUtc;
        BankName = Clean(bankName);
        BankBranch = Clean(bankBranch);
        AccountNumber = Clean(accountNumber);
        SayadId = Clean(sayadId);
        Note = Clean(note);
        return Result.Ok();
    }

    /// <summary>Tombstones an unfiled cheque; never erases its audit history.</summary>
    public Result DeleteDraft()
    {
        if (State != ChequeState.Registered || Deleted)
            return Result.Fail(ErrorCodes.ChequeNotEditable);
        Deleted = true;
        return Result.Ok();
    }

    /// <summary>Files a registered cheque into the portfolio, or returns a bounced one to it.</summary>
    public Result MoveToPortfolio(string actorUserId, IClock clock) =>
        Transition(ChequeState.InPortfolio, actorUserId, clock, null);

    /// <summary>Hands the cheque to the bank for collection or payment.</summary>
    public Result Deposit(string actorUserId, IClock clock) =>
        Transition(ChequeState.Deposited, actorUserId, clock, null);

    /// <summary>
    /// Records that the bank honoured the cheque and raises <see cref="ChequeClearedEvent"/>, which
    /// <see cref="PostingRules.ForChequeCleared"/> turns into the bank/cheque-account entry.
    /// </summary>
    public Result MarkCleared(string actorUserId, IClock clock)
    {
        var moved = Transition(ChequeState.Cleared, actorUserId, clock, null);
        if (moved.IsFailure)
        {
            return moved;
        }

        var now = clock.UtcNow;
        Raise(new ChequeClearedEvent(
            IdGenerator.NewId(now), TenantId, now, Id, Direction, Amount.Minor, Amount.Currency));
        return Result.Ok();
    }

    /// <summary>
    /// Records that the bank refused the cheque and raises <see cref="ChequeBouncedEvent"/>. The
    /// reason is mandatory — a bounce without an explanation is not chaseable.
    /// </summary>
    public Result MarkBounced(string actorUserId, string reason, IClock clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var moved = Transition(ChequeState.Bounced, actorUserId, clock, reason);
        if (moved.IsFailure)
        {
            return moved;
        }

        var now = clock.UtcNow;
        Raise(new ChequeBouncedEvent(
            IdGenerator.NewId(now), TenantId, now, Id, Direction, Amount.Minor, Amount.Currency, reason.Trim()));
        return Result.Ok();
    }

    /// <summary>Hands the cheque back to the counterparty. Terminal; the reason is mandatory.</summary>
    public Result Return(string actorUserId, string reason, IClock clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return Transition(ChequeState.Returned, actorUserId, clock, reason);
    }

    /// <summary>Voids a cheque that never went to the bank. Terminal; the reason is mandatory.</summary>
    public Result Cancel(string actorUserId, string reason, IClock clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return Transition(ChequeState.Cancelled, actorUserId, clock, reason);
    }

    /// <summary>Records that a bounced cheque was settled in cash instead. Terminal.</summary>
    public Result SettleInCash(string actorUserId, IClock clock) =>
        Transition(ChequeState.Settled, actorUserId, clock, null);

    /// <summary>Links the journal entry that recognised the cheque in the ledger.</summary>
    public Result LinkJournalEntry(string journalEntryId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(journalEntryId);

        JournalEntryId = journalEntryId.Trim();
        return Result.Ok();
    }

    /// <summary><see langword="true"/> when the machine allows <paramref name="from"/> → <paramref name="to"/>.</summary>
    public static bool IsTransitionAllowed(ChequeState from, ChequeState to) => (from, to) switch
    {
        (ChequeState.Registered, ChequeState.InPortfolio) => true,
        (ChequeState.Registered, ChequeState.Cancelled) => true,
        (ChequeState.InPortfolio, ChequeState.Deposited) => true,
        (ChequeState.InPortfolio, ChequeState.Returned) => true,
        (ChequeState.InPortfolio, ChequeState.Cancelled) => true,
        (ChequeState.Deposited, ChequeState.Cleared) => true,
        (ChequeState.Deposited, ChequeState.Bounced) => true,
        (ChequeState.Bounced, ChequeState.InPortfolio) => true,
        (ChequeState.Bounced, ChequeState.Returned) => true,
        (ChequeState.Bounced, ChequeState.Settled) => true,
        _ => false,
    };

    /// <summary>
    /// The single gate every state change goes through: it validates the machine, appends the
    /// history entry and raises <see cref="ChequeStateChangedEvent"/> — so no transition can ever
    /// happen without leaving a trace.
    /// </summary>
    private Result Transition(ChequeState target, string actorUserId, IClock clock, string? reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);
        ArgumentNullException.ThrowIfNull(clock);

        if (!IsTransitionAllowed(State, target))
        {
            return Result.Fail(
                ErrorCodes.ChequeInvalidTransition,
                $"Cheque '{ChequeNumber}' cannot move {State} -> {target}.");
        }

        var now = clock.UtcNow;
        var from = State;
        State = target;

        _history.Add(new ChequeEvent(
            IdGenerator.NewId(now), from, target, now, actorUserId, Clean(reason)));

        Raise(new ChequeStateChangedEvent(
            IdGenerator.NewId(now), TenantId, now, Id, from, target));
        return Result.Ok();
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
