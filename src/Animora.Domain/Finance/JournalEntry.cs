using Animora.Domain.Events;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Domain.ValueObjects;

namespace Animora.Domain.Finance;

/// <summary>What caused a journal entry to be written.</summary>
public enum JournalSource
{
    /// <summary>An invoice was posted.</summary>
    Sales = 0,

    /// <summary>Money was received against an invoice.</summary>
    Payment = 1,

    /// <summary>Money was returned to a payer.</summary>
    Refund = 2,

    /// <summary>A purchase / supplier liability.</summary>
    Purchase = 3,

    /// <summary>A goods receipt brought stock in.</summary>
    Receipt = 4,

    /// <summary>A stock movement (COGS, loss, adjustment).</summary>
    Inventory = 5,

    /// <summary>A cash session close (drawer variance).</summary>
    Cashier = 6,

    /// <summary>A cheque lifecycle transition.</summary>
    Cheque = 7,

    /// <summary>An operating expense.</summary>
    Expense = 8,

    /// <summary>The closing entry of an accounting period.</summary>
    PeriodClose = 9,

    /// <summary>Typed by an accountant.</summary>
    Manual = 10,

    /// <summary>The mirrored correction of an earlier entry.</summary>
    Reversal = 11,
}

/// <summary>
/// One balanced double entry of the general ledger — the accounting backbone of the product.
/// </summary>
/// <remarks>
/// <para>
/// <b>[INVARIANT] Append-only forever (A-RULE 002 / R-B22, ARCHITECTURE.md §8.5 and §11.1).</b>
/// This type has <b>no mutator</b>, no delete and no business soft-delete: every property is
/// <c>private init</c> and <see cref="Create"/> is the only constructor path. A correction is a
/// <b>new</b> entry produced by <see cref="CreateReversal"/> with the debits and credits swapped
/// and <see cref="ReversesEntryId"/> pointing at the original — never an edit, never an overwrite
/// (A-RULE 003: the correction always carries a reason in <see cref="Memo"/> and the link to what
/// it reverses). Because no row is ever updated, a ledger row cannot produce a sync conflict by
/// construction.
/// </para>
/// <para>
/// <b>[INVARIANT] Balanced double entry.</b> <see cref="Create"/> refuses anything that is not a
/// valid entry: fewer than two lines (<c>JOURNAL_TOO_FEW_LINES</c>), a non-positive line amount
/// (<c>JOURNAL_LINE_AMOUNT_INVALID</c>), more than one currency
/// (<c>JOURNAL_CURRENCY_MIXED</c>), a line naming a non-postable account
/// (<c>ACCOUNT_NOT_POSTABLE</c>) or an unnamed account (<c>ACCOUNT_UNKNOWN</c>), and finally
/// <c>Σ Debit ≠ Σ Credit</c> (<c>JOURNAL_UNBALANCED</c>). The whole check is a single
/// <c>for</c> pass over the lines (PERFORMANCE.md §2).
/// </para>
/// <para>
/// <b>Period gate.</b> Every write path calls
/// <see cref="AccountingPeriod.EnsurePostable(AccountingPeriod)"/> before creating an entry, so a
/// closed or locked period can never receive one; a post-close correction belongs to the next open
/// period.
/// </para>
/// <para>
/// Multi-currency ledgers are out of scope for the product: one entry is always expressed in one
/// currency, and <c>JOURNAL_CURRENCY_MIXED</c> is what says so.
/// </para>
/// </remarks>
public sealed class JournalEntry : AggregateRoot
{
    private readonly List<JournalLine> _lines = [];

    /// <summary>EF Core materialization constructor. Not for business code.</summary>
    private JournalEntry()
    {
    }

    /// <summary>
    /// The provisional entry number a freshly built entry carries until the Application layer stamps the
    /// gapless sequential value on it. It is deliberately a reserved, non-sequential marker that can never
    /// collide with a real <c>PREFIX-YY-NNNNNN</c> number, so <see cref="FinalizeEntryNumber"/> can tell an
    /// un-numbered entry apart from a settled one and never re-number a real value. An entry must never reach
    /// the store still carrying it — the finalize pass runs on every write path before save.
    /// </summary>
    public const string PendingNumber = "PENDING";

    /// <summary>Per-tenant sequential entry number, assigned by the Application layer.</summary>
    public string EntryNumber { get; private set; } = string.Empty;

    /// <summary><see langword="true"/> while the entry still carries its provisional <see cref="PendingNumber"/>.</summary>
    public bool HasPendingNumber => string.Equals(EntryNumber, PendingNumber, StringComparison.Ordinal);

    /// <summary>When the entry was written to the ledger, UTC.</summary>
    public DateTimeOffset PostedUtc { get; private init; }

    /// <summary>The accounting date the entry belongs to, UTC; may precede <see cref="PostedUtc"/>.</summary>
    public DateTimeOffset EffectiveUtc { get; private init; }

    /// <summary>The <see cref="AccountingPeriod.PeriodCode"/> this entry falls in.</summary>
    public string PeriodId { get; private init; } = string.Empty;

    /// <summary>What caused the entry.</summary>
    public JournalSource Source { get; private init; }

    /// <summary>The kind of business entity behind it, e.g. <c>"Invoice"</c>; audit trail only.</summary>
    public string? SourceEntityType { get; private init; }

    /// <summary>The id of that entity.</summary>
    public string? SourceEntityId { get; private init; }

    /// <summary>The business command that produced it — the idempotency key (A-RULE 006).</summary>
    public string? OperationId { get; private init; }

    /// <summary>Narration; for a reversal it carries the mandatory correction reason.</summary>
    public string Memo { get; private init; } = string.Empty;

    /// <summary>Who posted it.</summary>
    public string PostedByUserId { get; private init; } = string.Empty;

    /// <summary>The entry this one mirrors; <see langword="null"/> for an original entry.</summary>
    public string? ReversesEntryId { get; private init; }

    /// <summary>The postings, in the order the rule produced them.</summary>
    public IReadOnlyList<JournalLine> Lines => _lines;

    /// <summary><see langword="true"/> when this entry corrects an earlier one.</summary>
    public bool IsReversal => ReversesEntryId is not null;

    /// <summary>Σ of the debit lines — equal to the credit total by construction.</summary>
    public Money TotalDebit { get; private init; }

    /// <summary>ISO-4217 code every line of this entry is expressed in.</summary>
    public string Currency => TotalDebit.Currency;

    /// <summary>
    /// The only way to create a journal entry. Validates the double-entry invariants in a single
    /// pass and raises <see cref="JournalEntryPostedEvent"/> on success.
    /// </summary>
    /// <param name="tenantId">Owning tenant.</param>
    /// <param name="entryNumber">Per-tenant sequential number, assigned by the Application layer.</param>
    /// <param name="periodId">The <see cref="AccountingPeriod.PeriodCode"/> the entry falls in.</param>
    /// <param name="source">What caused the entry.</param>
    /// <param name="lines">The postings; at least two, one currency, every amount strictly positive.</param>
    /// <param name="memo">Narration; mandatory, because an unexplained ledger row is not auditable.</param>
    /// <param name="postedByUserId">Who posted it.</param>
    /// <param name="postedUtc">When it was written, UTC.</param>
    /// <param name="effectiveUtc">The accounting date; defaults to <paramref name="postedUtc"/>.</param>
    /// <param name="sourceEntityType">The kind of business entity behind it.</param>
    /// <param name="sourceEntityId">The id of that entity.</param>
    /// <param name="operationId">The business command that produced it (idempotency key).</param>
    /// <param name="reversesEntryId">The entry this one mirrors, when it is a correction.</param>
    public static Result<JournalEntry> Create(
        string tenantId,
        string entryNumber,
        string periodId,
        JournalSource source,
        IReadOnlyList<JournalLine> lines,
        string memo,
        string postedByUserId,
        DateTimeOffset postedUtc,
        DateTimeOffset? effectiveUtc = null,
        string? sourceEntityType = null,
        string? sourceEntityId = null,
        string? operationId = null,
        string? reversesEntryId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(periodId);
        ArgumentException.ThrowIfNullOrWhiteSpace(memo);
        ArgumentException.ThrowIfNullOrWhiteSpace(postedByUserId);
        ArgumentNullException.ThrowIfNull(lines);

        if (lines.Count < 2)
        {
            return Result<JournalEntry>.Fail(
                ErrorCodes.JournalTooFewLines,
                $"A journal entry needs at least two lines; got {lines.Count}.");
        }

        var currency = lines[0].Amount.Currency;
        var balance = 0L;
        var debitMinor = 0L;

        // One pass validates amount, currency, account and balance together (PERFORMANCE.md §2).
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];

            if (line.Amount.Minor <= 0L)
            {
                return Result<JournalEntry>.Fail(
                    ErrorCodes.JournalLineAmountInvalid,
                    $"Line {i} posts {line.Amount.Minor}; every journal line amount must be strictly positive.");
            }

            if (!string.Equals(line.Amount.Currency, currency, StringComparison.Ordinal))
            {
                return Result<JournalEntry>.Fail(
                    ErrorCodes.JournalCurrencyMixed,
                    $"Line {i} is in '{line.Amount.Currency}' but the entry is in '{currency}'; one entry is one currency.");
            }

            if (string.IsNullOrWhiteSpace(line.AccountCode))
            {
                return Result<JournalEntry>.Fail(
                    ErrorCodes.AccountUnknown,
                    $"Line {i} names no account.");
            }

            // A code absent from the default chart is a tenant-defined account: the write path
            // (A10) resolves it against the store. A default header account is refused here.
            var known = ChartOfAccounts.Find(line.AccountCode);
            if (known is { IsPostable: false })
            {
                return Result<JournalEntry>.Fail(
                    ErrorCodes.AccountNotPostable,
                    $"Line {i} posts to roll-up account '{line.AccountCode}', which takes no direct posting.");
            }

            balance += line.SignedMinor;
            if (line.IsDebit)
            {
                debitMinor += line.Amount.Minor;
            }
        }

        if (balance != 0L)
        {
            return Result<JournalEntry>.Fail(
                ErrorCodes.JournalUnbalanced,
                $"Debits minus credits is {balance} minor units; a journal entry must balance exactly.");
        }

        var effective = effectiveUtc ?? postedUtc;
        var entry = new JournalEntry
        {
            Id = IdGenerator.NewId(postedUtc),
            TenantId = tenantId,
            EntryNumber = entryNumber.Trim(),
            PeriodId = periodId.Trim(),
            Source = source,
            PostedUtc = postedUtc,
            EffectiveUtc = effective,
            Memo = memo.Trim(),
            PostedByUserId = postedByUserId,
            SourceEntityType = Clean(sourceEntityType),
            SourceEntityId = Clean(sourceEntityId),
            OperationId = Clean(operationId),
            ReversesEntryId = Clean(reversesEntryId),
            TotalDebit = new Money(debitMinor, currency),
        };

        entry._lines.AddRange(lines);
        entry.Raise(new JournalEntryPostedEvent(
            IdGenerator.NewId(postedUtc),
            tenantId,
            postedUtc,
            entry.Id,
            entry.PeriodId,
            source,
            debitMinor,
            currency));

        return entry;
    }

    /// <summary>
    /// The <b>only</b> correction path (A-RULE 002/003): a new entry whose lines are
    /// <paramref name="original"/>'s with debit and credit swapped, carrying the mandatory
    /// <paramref name="reason"/> in its memo and <see cref="ReversesEntryId"/> pointing at the
    /// original. The two entries together net to zero on every account.
    /// </summary>
    /// <param name="original">The entry being corrected. It is not touched.</param>
    /// <param name="reason">Why the correction is made; part of the audit trail.</param>
    /// <param name="clock">The injected clock — the reversal is posted "now".</param>
    /// <param name="entryNumber">
    /// The reversal's own sequential number; defaults to <c>"{original}-R"</c> when the caller has
    /// no allocator at hand.
    /// </param>
    /// <param name="periodId">
    /// The open period the reversal is posted into; defaults to the original's period. A
    /// post-close correction is posted into the <i>next open</i> period, never back into a closed one.
    /// </param>
    /// <param name="postedByUserId">Who corrects; defaults to the original poster.</param>
    /// <param name="operationId">The correcting command's idempotency key.</param>
    public static Result<JournalEntry> CreateReversal(
        JournalEntry original,
        string reason,
        IClock clock,
        string? entryNumber = null,
        string? periodId = null,
        string? postedByUserId = null,
        string? operationId = null)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentNullException.ThrowIfNull(clock);

        var now = clock.UtcNow;
        var mirrored = new JournalLine[original._lines.Count];
        for (var i = 0; i < mirrored.Length; i++)
        {
            mirrored[i] = original._lines[i].Mirror(IdGenerator.NewId(now));
        }

        // The reversal's own number is normally allocated by the deferred finalize pass just before save
        // (its caller leaves entryNumber null), so it is built with the provisional placeholder — exactly like
        // every other posting rule — and stamped gapless at the end of the transaction. A caller that already
        // holds a number (a self-contained correction with no allocator at hand) may still pass it explicitly.
        return Create(
            original.TenantId,
            string.IsNullOrWhiteSpace(entryNumber) ? PendingNumber : entryNumber,
            string.IsNullOrWhiteSpace(periodId) ? original.PeriodId : periodId,
            JournalSource.Reversal,
            mirrored,
            $"Reversal of entry {original.EntryNumber}: {reason.Trim()}",
            string.IsNullOrWhiteSpace(postedByUserId) ? original.PostedByUserId : postedByUserId,
            now,
            now,
            original.SourceEntityType,
            original.SourceEntityId,
            Clean(operationId),
            original.Id);
    }

    /// <summary>
    /// Stamps the gapless sequential number onto an entry that was built in this same unit of work with the
    /// provisional <see cref="PendingNumber"/>, deliberately deferred so the Application layer can allocate the
    /// serialized <c>DocumentCounters</c> value at the very end of the command transaction — shrinking the
    /// counter row-lock hold window to the imminent save/commit instead of the whole event-dispatch body.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A-RULE 002 (append-only) is preserved: this touches only the provisional identity field, never the
    /// frozen economics (lines, totals, period, source, memo). It is refused once the entry already carries a
    /// real number — <see cref="HasPendingNumber"/> is <see langword="false"/> — so a persisted, numbered entry
    /// can never be silently renumbered, and a double finalize is a loud failure rather than a silent gap.
    /// </para>
    /// <para>
    /// The number is not read by <see cref="Create"/>'s validation, the <see cref="JournalEntryPostedEvent"/>
    /// (which never carries it) or the entry's identity (<see cref="AggregateRoot.Id"/> is time-derived), so
    /// deferring and then stamping it keeps numbering identical, gapless and append-safe.
    /// </para>
    /// </remarks>
    /// <param name="number">The real gapless sequential number to stamp; never the placeholder.</param>
    public Result FinalizeEntryNumber(string number)
    {
        if (string.IsNullOrWhiteSpace(number)) return Result.Fail(ErrorCodes.ValidationFailed);
        if (string.Equals(number, PendingNumber, StringComparison.Ordinal))
            return Result.Fail(ErrorCodes.ValidationFailed, "The provisional placeholder is not a settleable entry number.");
        if (!HasPendingNumber)
            return Result.Fail(ErrorCodes.JournalImmutable,
                $"Journal entry '{Id}' already carries sequential number '{EntryNumber}'; it is never renumbered.");
        EntryNumber = number.Trim();
        return Result.Ok();
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
