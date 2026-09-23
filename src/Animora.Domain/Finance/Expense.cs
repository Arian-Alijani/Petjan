using Animora.Domain.Events;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Sales;
using Animora.Domain.Time;
using Animora.Domain.ValueObjects;

namespace Animora.Domain.Finance;

/// <summary>Lifecycle state of an expense.</summary>
public enum ExpenseState
{
    /// <summary>Being composed; still editable.</summary>
    Draft = 0,

    /// <summary>Posted to the ledger. Immutable — a correction is a reversing expense.</summary>
    Posted = 1,

    /// <summary>Offset by a later reversing expense. Terminal.</summary>
    Reversed = 2,
}

/// <summary>
/// An operating cost of the clinic: rent, salary, supplies, and so on.
/// </summary>
/// <remarks>
/// <para>
/// <b>[INVARIANT] Posting is one-way (A-RULE 002).</b> <see cref="Post"/> moves
/// <see cref="ExpenseState.Draft"/> → <see cref="ExpenseState.Posted"/> and raises
/// <see cref="ExpensePostedEvent"/>, which <see cref="PostingRules.ForExpensePosted"/> turns into
/// the journal entry. From then on every mutator returns <c>EXPENSE_IMMUTABLE</c>: a correction is
/// a <b>new</b> expense with <see cref="ReversesExpenseId"/> set and a negative-effect reversing
/// journal entry, never an edit of the posted row (A-RULE 003).
/// </para>
/// <para>
/// The expense names a <see cref="CategoryCode"/>, not an account;
/// <see cref="ExpenseCategories.AccountCodeFor(string)"/> resolves the debit account at posting time.
/// </para>
/// </remarks>
public sealed class Expense : AggregateRoot
{
    /// <summary>EF Core materialization constructor. Not for business code.</summary>
    private Expense()
    {
    }

    /// <summary>Per-tenant sequential expense number, assigned by the Application layer.</summary>
    public string ExpenseNumber { get; private set; } = string.Empty;

    /// <summary>One of <see cref="ExpenseCategories"/>; resolves the debit account.</summary>
    public string CategoryCode { get; private set; } = string.Empty;

    /// <summary>What the money was spent on, as shown in the expenses grid.</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>The amount spent; strictly positive.</summary>
    public Money Amount { get; private set; }

    /// <summary>When the cost was incurred, UTC — the accounting date, not the entry date.</summary>
    public DateTimeOffset IncurredUtc { get; private set; }

    /// <summary>Current lifecycle state.</summary>
    public ExpenseState State { get; private set; } = ExpenseState.Draft;

    /// <summary>How it was settled; <see langword="null"/> when still unpaid (credit → payable).</summary>
    public PaymentMethod? SettledMethod { get; private set; }

    /// <summary>The supplier paid, when there is one.</summary>
    public string? SupplierId { get; private set; }

    /// <summary>The outbound cheque that settles it, when settled by cheque.</summary>
    public string? ChequeId { get; private set; }

    /// <summary>The stored receipt scan / photo (Valet-Key gated — A-RULE 020).</summary>
    public string? ReceiptFileId { get; private set; }

    /// <summary>The journal entry produced at posting.</summary>
    public string? JournalEntryId { get; private set; }

    /// <summary>Who created the expense.</summary>
    public string CreatedByUserId { get; private set; } = string.Empty;

    /// <summary>Free-form note, kept for the audit trail.</summary>
    public string? Note { get; private set; }

    /// <summary>The posted expense this one offsets; <see langword="null"/> for an original.</summary>
    public string? ReversesExpenseId { get; private set; }

    /// <summary><see langword="true"/> once the expense may no longer change.</summary>
    public bool IsImmutable => State != ExpenseState.Draft;

    /// <summary><see langword="true"/> when this expense offsets an earlier one.</summary>
    public bool IsReversal => ReversesExpenseId is not null;

    /// <summary>The ledger account the debit lands on, resolved from <see cref="CategoryCode"/>.</summary>
    public string DebitAccountCode => ExpenseCategories.AccountCodeFor(CategoryCode);

    /// <summary>Creates a draft expense.</summary>
    /// <param name="tenantId">Owning tenant.</param>
    /// <param name="expenseNumber">Per-tenant sequential number, assigned by the Application layer.</param>
    /// <param name="categoryCode">One of <see cref="ExpenseCategories"/>.</param>
    /// <param name="title">What the money was spent on.</param>
    /// <param name="amount">The amount; strictly positive.</param>
    /// <param name="incurredUtc">When the cost was incurred, UTC.</param>
    /// <param name="createdByUserId">Who created it.</param>
    /// <param name="settledMethod">How it was settled; <see langword="null"/> leaves it payable.</param>
    /// <param name="supplierId">The supplier paid.</param>
    /// <param name="chequeId">The outbound cheque settling it.</param>
    /// <param name="receiptFileId">The stored receipt scan.</param>
    /// <param name="note">Audit-trail note.</param>
    /// <param name="reversesExpenseId">The posted expense this one offsets, when it is a correction.</param>
    public static Result<Expense> Create(
        string tenantId,
        string expenseNumber,
        string categoryCode,
        string title,
        Money amount,
        DateTimeOffset incurredUtc,
        string createdByUserId,
        PaymentMethod? settledMethod = null,
        string? supplierId = null,
        string? chequeId = null,
        string? receiptFileId = null,
        string? note = null,
        string? reversesExpenseId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(expenseNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(createdByUserId);

        if (amount.Minor <= 0L)
        {
            return Result<Expense>.Fail(
                ErrorCodes.ValidationFailed,
                $"Expense '{expenseNumber}' must carry a strictly positive amount; got {amount.Minor}.");
        }

        if (settledMethod == PaymentMethod.Cheque && string.IsNullOrWhiteSpace(chequeId))
        {
            return Result<Expense>.Fail(
                ErrorCodes.PaymentMethodReferenceRequired,
                "An expense settled by cheque must name its cheque record.");
        }

        return new Expense
        {
            Id = IdGenerator.NewId(incurredUtc),
            TenantId = tenantId,
            ExpenseNumber = expenseNumber.Trim(),
            CategoryCode = categoryCode.Trim(),
            Title = title.Trim(),
            Amount = amount,
            IncurredUtc = incurredUtc,
            State = ExpenseState.Draft,
            SettledMethod = settledMethod,
            SupplierId = Clean(supplierId),
            ChequeId = Clean(chequeId),
            ReceiptFileId = Clean(receiptFileId),
            CreatedByUserId = createdByUserId,
            Note = Clean(note),
            ReversesExpenseId = Clean(reversesExpenseId),
        };
    }

    /// <summary>Edits a draft expense. Refused once posted (<c>EXPENSE_IMMUTABLE</c>).</summary>
    public Result Edit(string categoryCode, string title, Money amount, string? note = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        var open = EnsureDraft();
        if (open.IsFailure)
        {
            return open;
        }

        if (amount.Minor <= 0L)
        {
            return Result.Fail(
                ErrorCodes.ValidationFailed,
                $"Expense '{ExpenseNumber}' must carry a strictly positive amount; got {amount.Minor}.");
        }

        CategoryCode = categoryCode.Trim();
        Title = title.Trim();
        Amount = amount;
        Note = Clean(note);
        return Result.Ok();
    }

    /// <summary>Records how a draft expense was settled. Refused once posted.</summary>
    public Result SetSettlement(PaymentMethod? method, string? supplierId = null, string? chequeId = null)
    {
        var open = EnsureDraft();
        if (open.IsFailure)
        {
            return open;
        }

        if (method == PaymentMethod.Cheque && string.IsNullOrWhiteSpace(chequeId))
        {
            return Result.Fail(
                ErrorCodes.PaymentMethodReferenceRequired,
                "An expense settled by cheque must name its cheque record.");
        }

        SettledMethod = method;
        SupplierId = Clean(supplierId) ?? SupplierId;
        ChequeId = Clean(chequeId);
        return Result.Ok();
    }

    /// <summary>Attaches the stored receipt scan to a draft expense.</summary>
    public Result AttachReceipt(string? receiptFileId)
    {
        var open = EnsureDraft();
        if (open.IsFailure)
        {
            return open;
        }

        ReceiptFileId = Clean(receiptFileId);
        return Result.Ok();
    }

    /// <summary>
    /// Posts the expense — one-way — and raises <see cref="ExpensePostedEvent"/>, which
    /// <see cref="PostingRules.ForExpensePosted"/> translates into the journal entry.
    /// </summary>
    public Result Post(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        var open = EnsureDraft();
        if (open.IsFailure)
        {
            return open;
        }

        State = ExpenseState.Posted;

        var now = clock.UtcNow;
        Raise(new ExpensePostedEvent(
            IdGenerator.NewId(now),
            TenantId,
            now,
            Id,
            CategoryCode,
            Amount.Minor,
            Amount.Currency,
            SettledMethod));
        return Result.Ok();
    }

    /// <summary>
    /// Marks a posted expense as offset by a later reversing expense. The amounts are never
    /// touched — the pair of rows is the correction.
    /// </summary>
    public Result MarkReversed()
    {
        if (State != ExpenseState.Posted)
        {
            return Result.Fail(
                ErrorCodes.ExpenseImmutable,
                $"Expense '{ExpenseNumber}' is {State}; only a posted expense can be marked reversed.");
        }

        State = ExpenseState.Reversed;
        return Result.Ok();
    }

    /// <summary>Links the journal entry produced at posting.</summary>
    public Result LinkJournalEntry(string journalEntryId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(journalEntryId);

        JournalEntryId = journalEntryId.Trim();
        return Result.Ok();
    }

    private Result EnsureDraft() =>
        IsImmutable
            ? Result.Fail(
                ErrorCodes.ExpenseImmutable,
                $"Expense '{ExpenseNumber}' is {State}; a correction is a reversing expense, not an edit.")
            : Result.Ok();

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
