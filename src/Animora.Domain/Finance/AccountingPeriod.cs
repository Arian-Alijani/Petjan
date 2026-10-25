using Animora.Domain.Events;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Time;

namespace Animora.Domain.Finance;

/// <summary>Lifecycle state of an accounting period.</summary>
public enum PeriodState
{
    /// <summary>Accepting journal entries.</summary>
    Open = 0,

    /// <summary>Close in progress; the closing entry is being prepared.</summary>
    Closing = 1,

    /// <summary>Closed: no further entry may be posted into it.</summary>
    Closed = 2,

    /// <summary>Closed and archived by an administrator. Terminal.</summary>
    Locked = 3,
}

/// <summary>
/// One accounting period — normally a Jalali month — and the gate that decides whether the ledger
/// still accepts entries for it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Jalali stays out of the Domain.</b> <see cref="PeriodCode"/> is an <i>opaque string key</i>
/// such as <c>"1405-07"</c>, formatted by the Desktop; the Domain never computes, parses or
/// validates a Jalali date (CONVENTIONS §2 — the Domain references nothing, and Persian calendar
/// handling is presentation-layer work). The authoritative boundaries are the UTC
/// <see cref="StartUtc"/>/<see cref="EndUtc"/> instants.
/// </para>
/// <para><b>State machine — strictly forward, no reopen:</b></para>
/// <code>
/// Open -> Closing -> Closed -> Locked
/// </code>
/// <para>
/// There is deliberately no path back to <see cref="PeriodState.Open"/>: a correction discovered
/// after the close is a reversing entry in the <i>next</i> open period (A-RULE 002/003), never a
/// retroactive edit of a closed book.
/// </para>
/// <para>
/// <b>Close blockers.</b> A period may not close while business intent is still in flight: any
/// unsettled command, any open cash session or any draft invoice returns
/// <c>PERIOD_CLOSE_BLOCKED</c> with a detail naming the blocker. Only the Application layer can
/// see those, so it passes the three flags in.
/// </para>
/// </remarks>
public sealed class AccountingPeriod : AggregateRoot
{
    /// <summary>EF Core materialization constructor. Not for business code.</summary>
    private AccountingPeriod()
    {
    }

    /// <summary>
    /// The opaque per-tenant period key, e.g. <c>"1405-07"</c>. Formatted by the Desktop; the
    /// Domain treats it as a string and nothing more.
    /// </summary>
    public string PeriodCode { get; private set; } = string.Empty;

    /// <summary>First instant of the period, UTC (inclusive).</summary>
    public DateTimeOffset StartUtc { get; private set; }

    /// <summary>Last instant of the period, UTC (exclusive upper bound).</summary>
    public DateTimeOffset EndUtc { get; private set; }

    /// <summary>Current lifecycle state.</summary>
    public PeriodState State { get; private set; } = PeriodState.Open;

    /// <summary>When the period was closed, UTC; <see langword="null"/> until then.</summary>
    public DateTimeOffset? ClosedUtc { get; private set; }

    /// <summary>Who closed it.</summary>
    public string? ClosedByUserId { get; private set; }

    /// <summary>The closing <see cref="JournalEntry"/>; mandatory to reach <see cref="PeriodState.Closed"/>.</summary>
    public string? ClosingEntryId { get; private set; }

    /// <summary>Free-form note kept with the close, for the audit trail.</summary>
    public string? Note { get; private set; }

    /// <summary><see langword="true"/> when the period no longer accepts journal entries.</summary>
    public bool IsPostingClosed => State is PeriodState.Closed or PeriodState.Locked;

    /// <summary>Creates an open period. <paramref name="endUtc"/> must be after <paramref name="startUtc"/>.</summary>
    /// <param name="tenantId">Owning tenant.</param>
    /// <param name="periodCode">The opaque period key, e.g. <c>"1405-07"</c>.</param>
    /// <param name="startUtc">First instant of the period, UTC.</param>
    /// <param name="endUtc">Exclusive upper bound of the period, UTC.</param>
    /// <param name="note">Optional note.</param>
    public static Result<AccountingPeriod> Create(
        string tenantId,
        string periodCode,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        string? note = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(periodCode);

        if (endUtc <= startUtc)
        {
            return Result<AccountingPeriod>.Fail(
                ErrorCodes.ValidationFailed,
                $"Period '{periodCode}' ends at or before it starts ({startUtc:O} → {endUtc:O}).");
        }

        var code = periodCode.Trim();
        return new AccountingPeriod
        {
            // Deterministic id keyed on (TenantId, PeriodCode): the ledger lazily opens the containing period on
            // first post on whichever node posts first (offline client or server), independently — not inside one
            // replayed command — so a random id would make the two diverge. The unique (TenantId, PeriodCode)
            // index then rejects the server's replicated row (same code, different id) forever, stalling sync and
            // surfacing as a CONCURRENCY_CONFLICT on the next financial command that touches the ledger. Deriving
            // the id from the natural key makes client, server and seeder converge on one id, so the replica
            // upserts in place (ON CONFLICT "Id") instead of colliding. The seeder opens the same period the same
            // way, so a seeded and a lazily-opened period are byte-for-byte identical, id included.
            Id = IdGenerator.ForNaturalKey(tenantId + ":" + code, startUtc),
            TenantId = tenantId,
            PeriodCode = code,
            StartUtc = startUtc,
            EndUtc = endUtc,
            State = PeriodState.Open,
            Note = Clean(note),
        };
    }

    /// <summary>
    /// The gate <b>every</b> <see cref="JournalEntry"/> write path calls before posting: returns
    /// <c>PERIOD_CLOSED</c> for a <see cref="PeriodState.Closed"/> or
    /// <see cref="PeriodState.Locked"/> period.
    /// </summary>
    /// <remarks>
    /// A <see cref="PeriodState.Closing"/> period still accepts entries — the closing entry itself
    /// is posted while the close is in progress.
    /// </remarks>
    public static Result EnsurePostable(AccountingPeriod period)
    {
        ArgumentNullException.ThrowIfNull(period);

        return period.IsPostingClosed
            ? Result.Fail(
                ErrorCodes.PeriodClosed,
                $"Period '{period.PeriodCode}' is {period.State}; post the correction into the next open period.")
            : Result.Ok();
    }

    /// <summary>Moves the period to <see cref="PeriodState.Closing"/> so the closing entry can be prepared.</summary>
    public Result BeginClose() => Transition(PeriodState.Closing);

    /// <summary>
    /// Closes the period once nothing is in flight, stamping the close audit fields and raising
    /// <see cref="PeriodClosedEvent"/>.
    /// </summary>
    /// <remarks>
    /// Returns <c>PERIOD_CLOSE_BLOCKED</c> — with a detail naming the blocker — when any of the
    /// three flags is <see langword="true"/>. The Application layer supplies them because only it
    /// can see the command queue, the drawers and the draft invoices.
    /// </remarks>
    /// <param name="hasUnsettledCommands">A command in this period has not settled with the server.</param>
    /// <param name="hasOpenCashSessions">A drawer opened in this period is still open.</param>
    /// <param name="hasDraftInvoices">An invoice of this period is still a draft.</param>
    /// <param name="clock">The injected clock.</param>
    /// <param name="closingEntryId">The closing <see cref="JournalEntry"/>; mandatory.</param>
    /// <param name="closedByUserId">Who closes the period.</param>
    /// <param name="note">Optional closing note.</param>
    public Result Close(
        bool hasUnsettledCommands,
        bool hasOpenCashSessions,
        bool hasDraftInvoices,
        IClock clock,
        string closingEntryId,
        string closedByUserId,
        string? note = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentException.ThrowIfNullOrWhiteSpace(closingEntryId);
        ArgumentException.ThrowIfNullOrWhiteSpace(closedByUserId);

        var blocker = hasUnsettledCommands
            ? "UNSETTLED_COMMANDS"
            : hasOpenCashSessions
                ? "OPEN_CASH_SESSIONS"
                : hasDraftInvoices
                    ? "DRAFT_INVOICES"
                    : null;

        if (blocker is not null)
        {
            return Result.Fail(
                ErrorCodes.PeriodCloseBlocked,
                $"Period '{PeriodCode}' cannot close: {blocker}.");
        }

        var moved = Transition(PeriodState.Closed);
        if (moved.IsFailure)
        {
            return moved;
        }

        var now = clock.UtcNow;
        ClosedUtc = now;
        ClosedByUserId = closedByUserId;
        ClosingEntryId = closingEntryId.Trim();
        Note = Clean(note) ?? Note;

        Raise(new PeriodClosedEvent(
            IdGenerator.NewId(now), TenantId, now, PeriodCode, ClosingEntryId));
        return Result.Ok();
    }

    /// <summary>Archives a closed period. Terminal — there is no path back.</summary>
    public Result Lock() => Transition(PeriodState.Locked);

    /// <summary>
    /// <see langword="true"/> when the strictly forward machine allows <paramref name="from"/> →
    /// <paramref name="to"/>. Nothing returns to <see cref="PeriodState.Open"/>.
    /// </summary>
    public static bool IsTransitionAllowed(PeriodState from, PeriodState to) => (from, to) switch
    {
        (PeriodState.Open, PeriodState.Closing) => true,
        (PeriodState.Closing, PeriodState.Closed) => true,
        (PeriodState.Closed, PeriodState.Locked) => true,
        _ => false,
    };

    /// <summary>The single gate every state change goes through.</summary>
    private Result Transition(PeriodState target)
    {
        if (!IsTransitionAllowed(State, target))
        {
            return Result.Fail(
                ErrorCodes.PeriodInvalidTransition,
                $"Period '{PeriodCode}' cannot move {State} -> {target}.");
        }

        State = target;
        return Result.Ok();
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
