using Animora.Domain.Events;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Domain.ValueObjects;

namespace Animora.Domain.Cashier;

/// <summary>Lifecycle state of a cash session.</summary>
public enum CashSessionState
{
    /// <summary>The drawer is in use; movements may be recorded.</summary>
    Open = 0,

    /// <summary>The cashier has declared a closing count; the drawer no longer takes movements.</summary>
    Closing = 1,

    /// <summary>Closed and final; immutable.</summary>
    Closed = 2,

    /// <summary>Checked and accepted by a supervisor. Terminal.</summary>
    Reconciled = 3,
}

/// <summary>
/// The cash-session aggregate root: one cashier's drawer, from the opening float to the reconciled
/// close.
/// </summary>
/// <remarks>
/// <para>The state machine is strictly forward:</para>
/// <code>
/// Open -> Closing -> Closed -> Reconciled
/// </code>
/// <para>Its invariants:</para>
/// <list type="bullet">
/// <item>
/// Only one <see cref="CashSessionState.Open"/> session may exist per
/// <c>(TenantId, CashierUserId, TerminalCode)</c>. The store enforces it with a unique filtered
/// index (A09); the caller checks it up front through
/// <see cref="EnsureNoOpenSession(bool)"/> (<c>CASH_SESSION_ALREADY_OPEN</c>).
/// </item>
/// <item>
/// <c>ExpectedClosing = OpeningFloat + Σ movement amounts</c> and
/// <c>Variance = DeclaredClosing - ExpectedClosing</c>. A non-zero variance is <b>allowed</b> —
/// drawers do go wrong — but it must be explained: closing with a variance and no note returns
/// <c>CASH_VARIANCE_NOTE_REQUIRED</c>.
/// </item>
/// <item>
/// Closing is refused while any linked invoice is still
/// <see cref="Sales.SettlementStatus.PendingSettlement"/>
/// (<c>CASH_SESSION_UNSETTLED_COMMANDS</c>) — the caller supplies that flag, since only the
/// Application layer can see the command queue.
/// </item>
/// <item>
/// A closed session is immutable (<c>CASH_SESSION_CLOSED</c>). A correction is a movement in the
/// <i>next</i> session referencing the prior one, never a retroactive edit (A-RULE 003).
/// </item>
/// </list>
/// </remarks>
public sealed class CashSession : AggregateRoot
{
    private readonly List<CashMovement> _movements = [];

    /// <summary>EF Core materialization constructor. Not for business code.</summary>
    private CashSession()
    {
    }

    /// <summary>Per-tenant sequential session number, assigned by the Application layer.</summary>
    public string SessionNumber { get; private set; } = string.Empty;

    /// <summary>The cashier who owns the drawer.</summary>
    public string CashierUserId { get; private set; } = string.Empty;

    /// <summary>The physical terminal / till the drawer belongs to.</summary>
    public string? TerminalCode { get; private set; }

    /// <summary>Current lifecycle state.</summary>
    public CashSessionState State { get; private set; } = CashSessionState.Open;

    /// <summary>When the drawer was opened, UTC.</summary>
    public DateTimeOffset OpenedUtc { get; private set; }

    /// <summary>When it was closed, UTC; <see langword="null"/> until then.</summary>
    public DateTimeOffset? ClosedUtc { get; private set; }

    /// <summary>The change float the drawer started with.</summary>
    public Money OpeningFloat { get; private set; }

    /// <summary>What the cashier counted at close.</summary>
    public Money DeclaredClosing { get; private set; }

    /// <summary><c>OpeningFloat + Σ movement amounts</c>: what should be in the drawer.</summary>
    public Money ExpectedClosing { get; private set; }

    /// <summary><c>DeclaredClosing - ExpectedClosing</c>: over (positive) or short (negative).</summary>
    public Money Variance { get; private set; }

    /// <summary>The cashier's explanation of the variance; mandatory when it is non-zero.</summary>
    public string? CloseNote { get; private set; }

    /// <summary>Who closed the drawer.</summary>
    public string? ClosedByUserId { get; private set; }

    /// <summary>Who accepted the reconciliation.</summary>
    public string? ReconciledByUserId { get; private set; }

    /// <summary>The drawer's ledger, oldest first.</summary>
    public IReadOnlyList<CashMovement> Movements => _movements;

    /// <summary><see langword="true"/> once the session may no longer change.</summary>
    public bool IsClosed => State is CashSessionState.Closed or CashSessionState.Reconciled;

    /// <summary>Currency every amount of this session is expressed in.</summary>
    public string Currency => OpeningFloat.Currency;

    /// <summary>
    /// Opens a drawer with its float and raises <see cref="CashSessionOpenedEvent"/>. The caller
    /// must have verified through <see cref="EnsureNoOpenSession(bool)"/> that this cashier has no
    /// other open session on this terminal.
    /// </summary>
    /// <param name="tenantId">Owning tenant.</param>
    /// <param name="sessionNumber">Per-tenant sequential number, assigned by the Application layer.</param>
    /// <param name="cashierUserId">The cashier opening the drawer.</param>
    /// <param name="openingFloat">The change float the drawer starts with; never negative.</param>
    /// <param name="openedUtc">When the drawer is opened, UTC.</param>
    /// <param name="terminalCode">The physical terminal / till.</param>
    public static Result<CashSession> Open(
        string tenantId,
        string sessionNumber,
        string cashierUserId,
        Money openingFloat,
        DateTimeOffset openedUtc,
        string? terminalCode = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(cashierUserId);

        if (openingFloat.IsNegative)
        {
            return Result<CashSession>.Fail(
                ErrorCodes.ValidationFailed,
                $"An opening float must not be negative; got {openingFloat.Minor}.");
        }

        var zero = Money.Zero(openingFloat.Currency);
        var session = new CashSession
        {
            Id = IdGenerator.NewId(openedUtc),
            TenantId = tenantId,
            SessionNumber = sessionNumber.Trim(),
            CashierUserId = cashierUserId,
            TerminalCode = Clean(terminalCode),
            State = CashSessionState.Open,
            OpenedUtc = openedUtc,
            OpeningFloat = openingFloat,
            DeclaredClosing = zero,
            ExpectedClosing = openingFloat,
            Variance = zero,
        };

        session.Raise(new CashSessionOpenedEvent(
            IdGenerator.NewId(openedUtc), tenantId, openedUtc, session.Id, cashierUserId));
        return session;
    }

    /// <summary>
    /// The open-session uniqueness guard: the caller queries whether this
    /// <c>(TenantId, CashierUserId, TerminalCode)</c> already has an open session and passes the
    /// answer here (<c>CASH_SESSION_ALREADY_OPEN</c>). The store's unique filtered index (A09) is
    /// the second, authoritative line of defence.
    /// </summary>
    public static Result EnsureNoOpenSession(bool anyOpenExists) =>
        anyOpenExists
            ? Result.Fail(
                ErrorCodes.CashSessionAlreadyOpen,
                "This cashier already has an open session on this terminal.")
            : Result.Ok();

    /// <summary>
    /// Appends a drawer movement and refreshes <see cref="ExpectedClosing"/>. Legal only while the
    /// session is <see cref="CashSessionState.Open"/> (<c>CASH_SESSION_CLOSED</c>).
    /// </summary>
    public Result AddMovement(CashMovement movement)
    {
        ArgumentNullException.ThrowIfNull(movement);

        if (State != CashSessionState.Open)
        {
            return Result.Fail(
                ErrorCodes.CashSessionClosed,
                $"Session '{Id}' is {State}; a movement can only be recorded while it is open.");
        }

        if (!string.Equals(movement.Amount.Currency, Currency, StringComparison.Ordinal))
        {
            return Result.Fail(
                ErrorCodes.ValidationFailed,
                $"Movement currency '{movement.Amount.Currency}' does not match session currency '{Currency}'.");
        }

        _movements.Add(movement);
        Recalculate();
        return Result.Ok();
    }

    /// <summary>
    /// Declares the counted cash and moves the session to <see cref="CashSessionState.Closing"/>.
    /// </summary>
    /// <remarks>
    /// Refused while <paramref name="hasUnsettled"/> says a linked invoice is still
    /// <see cref="Sales.SettlementStatus.PendingSettlement"/>
    /// (<c>CASH_SESSION_UNSETTLED_COMMANDS</c>) — a drawer is never reconciled against money whose
    /// fate the server has not decided. A non-zero variance is allowed but must carry a note
    /// (<c>CASH_VARIANCE_NOTE_REQUIRED</c>).
    /// </remarks>
    /// <param name="declared">What the cashier counted.</param>
    /// <param name="note">The explanation; mandatory when the variance is non-zero.</param>
    /// <param name="clock">The injected clock.</param>
    /// <param name="hasUnsettled"><see langword="true"/> when a linked invoice is still pending settlement.</param>
    public Result BeginClose(Money declared, string? note, IClock clock, bool hasUnsettled)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (State != CashSessionState.Open)
        {
            return Result.Fail(
                ErrorCodes.CashSessionClosed,
                $"Session '{Id}' is {State}; only an open session can begin closing.");
        }

        if (hasUnsettled)
        {
            return Result.Fail(
                ErrorCodes.CashSessionUnsettledCommands,
                $"Session '{Id}' still has commands awaiting settlement; it cannot be closed yet.");
        }

        if (!string.Equals(declared.Currency, Currency, StringComparison.Ordinal))
        {
            return Result.Fail(
                ErrorCodes.ValidationFailed,
                $"Declared currency '{declared.Currency}' does not match session currency '{Currency}'.");
        }

        Recalculate();
        var variance = declared.Subtract(ExpectedClosing);
        if (!variance.IsZero && string.IsNullOrWhiteSpace(note))
        {
            return Result.Fail(
                ErrorCodes.CashVarianceNoteRequired,
                $"Session '{Id}' has a variance of {variance.Minor}; a closing note is required.");
        }

        DeclaredClosing = declared;
        Variance = variance;
        CloseNote = Clean(note);
        return Transition(CashSessionState.Closing);
    }

    /// <summary>
    /// Finalizes the close: stamps <see cref="ClosedUtc"/>, freezes the session and raises
    /// <see cref="CashSessionClosedEvent"/>. Only legal from <see cref="CashSessionState.Closing"/>.
    /// </summary>
    public Result Close(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        var moved = Transition(CashSessionState.Closed);
        if (moved.IsFailure)
        {
            return moved;
        }

        var now = clock.UtcNow;
        ClosedUtc = now;
        ClosedByUserId = CashierUserId;

        Raise(new CashSessionClosedEvent(
            IdGenerator.NewId(now), TenantId, now, Id, Variance.Minor, Variance.Currency));
        return Result.Ok();
    }

    /// <summary>
    /// Records a supervisor's acceptance of the closed drawer. Only legal from
    /// <see cref="CashSessionState.Closed"/>.
    /// </summary>
    public Result Reconcile(string reconciledByUserId, IClock clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reconciledByUserId);
        ArgumentNullException.ThrowIfNull(clock);

        var moved = Transition(CashSessionState.Reconciled);
        if (moved.IsFailure)
        {
            return moved;
        }

        ReconciledByUserId = reconciledByUserId;
        return Result.Ok();
    }

    /// <summary><see langword="true"/> when the strictly forward machine allows <paramref name="from"/> → <paramref name="to"/>.</summary>
    public static bool IsTransitionAllowed(CashSessionState from, CashSessionState to) => (from, to) switch
    {
        (CashSessionState.Open, CashSessionState.Closing) => true,
        (CashSessionState.Closing, CashSessionState.Closed) => true,
        (CashSessionState.Closed, CashSessionState.Reconciled) => true,
        _ => false,
    };

    /// <summary>The single gate every state change goes through.</summary>
    private Result Transition(CashSessionState target)
    {
        if (!IsTransitionAllowed(State, target))
        {
            return Result.Fail(
                ErrorCodes.CashSessionClosed,
                $"Session '{Id}' cannot move {State} -> {target}.");
        }

        State = target;
        return Result.Ok();
    }

    /// <summary>
    /// Recomputes <see cref="ExpectedClosing"/> (and the variance when a count was declared) from
    /// the float and the movements. The <b>only</b> writer of the expected total.
    /// </summary>
    private void Recalculate()
    {
        var expected = OpeningFloat.Minor;
        for (var i = 0; i < _movements.Count; i++)
        {
            expected += _movements[i].Amount.Minor;
        }

        ExpectedClosing = new Money(expected, Currency);
        if (!DeclaredClosing.IsZero)
        {
            Variance = DeclaredClosing.Subtract(ExpectedClosing);
        }
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
