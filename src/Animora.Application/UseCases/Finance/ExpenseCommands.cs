using Animora.Application.Abstractions;
using Animora.Application.Auditing;
using Animora.Application.Commands;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.Validation;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Finance;
using Animora.Domain.Identity;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Sales;
using Animora.Domain.Time;
using Animora.Domain.ValueObjects;

namespace Animora.Application.UseCases.Finance;

internal static class ExpenseRules
{
    public const int MaxTitleLength = 200;
    public const int MaxNoteLength = 1000;

    public static ValueTask<IReadOnlyList<ValidationFailure>> Check(params ValidationFailure?[] checks) =>
        ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(checks.OfType<ValidationFailure>().ToArray());

    public static ValidationFailure? Category(string? code, string field) =>
        code is null || !ExpenseCategories.Contains(code) ? new(field, "EXPENSE_CATEGORY_INVALID") : null;

    /// <summary>Cheque settlement needs a cheque record; the expense screen settles cash/card/transfer/credit only.</summary>
    public static ValidationFailure? Method(PaymentMethod? method, string field) =>
        method is PaymentMethod.Cheque || (method is { } m && !Enum.IsDefined(m)) ? new(field, "EXPENSE_METHOD_INVALID") : null;

    /// <summary>Allocates a number, creates the draft and posts it (raising the ledger/fact event).</summary>
    public static async Task<Result<Expense>> CreateAndPostAsync(IExpenseRepository expenses, IExpenseNumberAllocator numbers,
        IClock clock, CommandContext context, string category, string title, long amountMinor, DateTimeOffset incurredUtc,
        PaymentMethod? method, string? supplierId, string? receiptFileId, string? note, string? reversesExpenseId,
        CancellationToken ct)
    {
        var number = await numbers.AllocateAsync(context.TenantId, incurredUtc, ct);
        var created = Expense.Create(context.TenantId, number, category, title, Money.Irt(amountMinor), incurredUtc,
            context.UserId, method, supplierId, null, receiptFileId, note, reversesExpenseId,
            id: IdGenerator.ForOperation(context.OperationId, 0, clock.UtcNow));
        if (created.IsFailure) return created;
        var expense = created.Value!;
        var posted = expense.Post(clock);
        if (posted.IsFailure) return Result<Expense>.Fail(posted.Error, posted.Detail);
        expenses.Add(expense);
        return Result<Expense>.Ok(expense);
    }
}

/// <summary>Records and immediately posts a one-off expense. Returns the expense id.</summary>
[RequiresPermission(PermissionCodes.FinancePost)]
[Auditable(AuditCategory.Financial, CommandTypes.FinanceExpensePost)]
public sealed record AddExpenseCommand(string CategoryCode, string Title, long AmountMinor, DateTimeOffset IncurredUtc,
    PaymentMethod? SettledMethod = PaymentMethod.Cash, string? SupplierId = null, string? ReceiptFileId = null,
    string? Note = null) : ICommand<string>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.FinanceExpensePost;
    public string AggregateTypeName => nameof(Expense);
    public string? AggregateIdOrNull => null;
}

public sealed class AddExpenseCommandValidator(IClock clock) : IValidator<AddExpenseCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(AddExpenseCommand c, CancellationToken ct) =>
        ExpenseRules.Check(ExpenseRules.Category(c.CategoryCode, nameof(c.CategoryCode)),
            Rules.Required(c.Title, nameof(c.Title)), Rules.MaxLength(c.Title, ExpenseRules.MaxTitleLength, nameof(c.Title)),
            Rules.MaxLength(c.Note, ExpenseRules.MaxNoteLength, nameof(c.Note)),
            Rules.PositiveMoney(Money.Irt(c.AmountMinor), nameof(c.AmountMinor)),
            Rules.NotFutureUtc(c.IncurredUtc, clock.UtcNow.AddDays(1), nameof(c.IncurredUtc)),
            ExpenseRules.Method(c.SettledMethod, nameof(c.SettledMethod)));
}

public sealed class AddExpenseCommandHandler(IExpenseRepository expenses, IExpenseNumberAllocator numbers, IClock clock)
    : ICommandHandler<AddExpenseCommand, string>
{
    public async Task<Result<string>> HandleAsync(AddExpenseCommand c, CommandContext context, CancellationToken ct)
    {
        var posted = await ExpenseRules.CreateAndPostAsync(expenses, numbers, clock, context, c.CategoryCode, c.Title,
            c.AmountMinor, c.IncurredUtc, c.SettledMethod, c.SupplierId, c.ReceiptFileId, c.Note, null, ct);
        return posted.IsFailure ? Result<string>.Fail(posted.Error, posted.Detail) : Result<string>.Ok(posted.Value!.Id);
    }
}

/// <summary>
/// Corrects a posted expense with a reversing expense (same amount, <c>ReversesExpenseId</c> set) and marks the
/// original reversed — never an edit-after-post (A-RULE 003). Returns the reversing expense id.
/// </summary>
[RequiresPermission(PermissionCodes.FinancePost)]
[Auditable(AuditCategory.Financial, CommandTypes.FinanceExpenseReverse)]
public sealed record ReverseExpenseCommand(string ExpenseId, string Reason) : ICommand<string>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.FinanceExpenseReverse;
    public string AggregateTypeName => nameof(Expense);
    public string? AggregateIdOrNull => ExpenseId;
}

public sealed class ReverseExpenseCommandValidator : IValidator<ReverseExpenseCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(ReverseExpenseCommand c, CancellationToken ct) =>
        ExpenseRules.Check(Rules.Required(c.ExpenseId, nameof(c.ExpenseId)), Rules.Required(c.Reason, nameof(c.Reason)),
            Rules.MaxLength(c.Reason, ExpenseRules.MaxNoteLength, nameof(c.Reason)));
}

public sealed class ReverseExpenseCommandHandler(IExpenseRepository expenses, IExpenseNumberAllocator numbers, IClock clock)
    : ICommandHandler<ReverseExpenseCommand, string>
{
    public async Task<Result<string>> HandleAsync(ReverseExpenseCommand c, CommandContext context, CancellationToken ct)
    {
        var original = await expenses.FindAsync(c.ExpenseId, ct);
        if (original is null || original.Deleted) return Result<string>.Fail(ErrorCodes.ExpenseNotFound);
        if (original.State != ExpenseState.Posted || original.IsReversal)
            return Result<string>.Fail(ErrorCodes.ExpenseNotReversible);
        var reversal = await ExpenseRules.CreateAndPostAsync(expenses, numbers, clock, context, original.CategoryCode,
            original.Title, original.Amount.Minor, clock.UtcNow, original.SettledMethod, original.SupplierId, null,
            c.Reason.Trim(), original.Id, ct);
        if (reversal.IsFailure) return Result<string>.Fail(reversal.Error, reversal.Detail);
        var marked = original.MarkReversed();
        return marked.IsFailure ? Result<string>.Fail(marked.Error, marked.Detail) : Result<string>.Ok(reversal.Value!.Id);
    }
}
