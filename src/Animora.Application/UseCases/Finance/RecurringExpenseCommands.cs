using Animora.Application.Abstractions;
using Animora.Application.Auditing;
using Animora.Application.Commands;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.UseCases.Finance.ReadModels;
using Animora.Application.Validation;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Finance;
using Animora.Domain.Identity;
using Animora.Domain.Results;
using Animora.Domain.Sales;
using Animora.Domain.Time;
using Animora.Domain.ValueObjects;

namespace Animora.Application.UseCases.Finance;

internal static class RecurringExpenseRules
{
    public static ValidationFailure? Day(int day, string field) =>
        Rules.Range(day, RecurringExpense.MinDayOfMonth, RecurringExpense.MaxDayOfMonth, field);
}

/// <summary>Defines a fixed monthly cost. Returns the new template id.</summary>
[RequiresPermission(PermissionCodes.FinancePost)]
[Auditable(AuditCategory.Financial, CommandTypes.FinanceRecurringAdd)]
public sealed record AddRecurringExpenseCommand(string CategoryCode, string Title, long AmountMinor, int DayOfMonth,
    string? SupplierId = null, string? Note = null) : ICommand<string>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.FinanceRecurringAdd;
    public string AggregateTypeName => nameof(RecurringExpense);
    public string? AggregateIdOrNull => null;
}

public sealed class AddRecurringExpenseCommandValidator : IValidator<AddRecurringExpenseCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(AddRecurringExpenseCommand c, CancellationToken ct) =>
        ExpenseRules.Check(ExpenseRules.Category(c.CategoryCode, nameof(c.CategoryCode)),
            Rules.Required(c.Title, nameof(c.Title)), Rules.MaxLength(c.Title, ExpenseRules.MaxTitleLength, nameof(c.Title)),
            Rules.MaxLength(c.Note, ExpenseRules.MaxNoteLength, nameof(c.Note)),
            Rules.PositiveMoney(Money.Irt(c.AmountMinor), nameof(c.AmountMinor)),
            RecurringExpenseRules.Day(c.DayOfMonth, nameof(c.DayOfMonth)));
}

public sealed class AddRecurringExpenseCommandHandler(IRecurringExpenseRepository repository, IClock clock)
    : ICommandHandler<AddRecurringExpenseCommand, string>
{
    public Task<Result<string>> HandleAsync(AddRecurringExpenseCommand c, CommandContext context, CancellationToken ct)
    {
        var created = RecurringExpense.Create(context.TenantId, c.CategoryCode, c.Title, Money.Irt(c.AmountMinor),
            c.DayOfMonth, clock.UtcNow, c.SupplierId, c.Note);
        if (created.IsFailure) return Task.FromResult(Result<string>.Fail(created.Error, created.Detail));
        repository.Add(created.Value!);
        return Task.FromResult(Result<string>.Ok(created.Value!.Id));
    }
}

/// <summary>Edits a template; the next payment uses the new values, posted expenses are untouched.</summary>
[RequiresPermission(PermissionCodes.FinancePost)]
[Auditable(AuditCategory.Financial, CommandTypes.FinanceRecurringEdit)]
public sealed record EditRecurringExpenseCommand(string RecurringExpenseId, string CategoryCode, string Title,
    long AmountMinor, int DayOfMonth, string? SupplierId = null, string? Note = null) : ICommand<string>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.FinanceRecurringEdit;
    public string AggregateTypeName => nameof(RecurringExpense);
    public string? AggregateIdOrNull => RecurringExpenseId;
}

public sealed class EditRecurringExpenseCommandValidator : IValidator<EditRecurringExpenseCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(EditRecurringExpenseCommand c, CancellationToken ct) =>
        ExpenseRules.Check(Rules.Required(c.RecurringExpenseId, nameof(c.RecurringExpenseId)),
            ExpenseRules.Category(c.CategoryCode, nameof(c.CategoryCode)),
            Rules.Required(c.Title, nameof(c.Title)), Rules.MaxLength(c.Title, ExpenseRules.MaxTitleLength, nameof(c.Title)),
            Rules.MaxLength(c.Note, ExpenseRules.MaxNoteLength, nameof(c.Note)),
            Rules.PositiveMoney(Money.Irt(c.AmountMinor), nameof(c.AmountMinor)),
            RecurringExpenseRules.Day(c.DayOfMonth, nameof(c.DayOfMonth)));
}

public sealed class EditRecurringExpenseCommandHandler(IRecurringExpenseRepository repository)
    : ICommandHandler<EditRecurringExpenseCommand, string>
{
    public async Task<Result<string>> HandleAsync(EditRecurringExpenseCommand c, CommandContext context, CancellationToken ct)
    {
        var template = await repository.FindAsync(c.RecurringExpenseId, ct);
        if (template is null || template.Deleted) return Result<string>.Fail(ErrorCodes.RecurringExpenseNotFound);
        var edited = template.Edit(c.CategoryCode, c.Title, Money.Irt(c.AmountMinor), c.DayOfMonth, c.SupplierId, c.Note);
        return edited.IsFailure ? Result<string>.Fail(edited.Error, edited.Detail) : Result<string>.Ok(template.Id);
    }
}

/// <summary>Activates or deactivates a template (inactive ones are never reminded or payable).</summary>
[RequiresPermission(PermissionCodes.FinancePost)]
[Auditable(AuditCategory.Financial, CommandTypes.FinanceRecurringToggle)]
public sealed record ToggleRecurringExpenseCommand(string RecurringExpenseId, bool IsActive) : ICommand<string>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.FinanceRecurringToggle;
    public string AggregateTypeName => nameof(RecurringExpense);
    public string? AggregateIdOrNull => RecurringExpenseId;
}

public sealed class ToggleRecurringExpenseCommandValidator : IValidator<ToggleRecurringExpenseCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(ToggleRecurringExpenseCommand c, CancellationToken ct) =>
        ExpenseRules.Check(Rules.Required(c.RecurringExpenseId, nameof(c.RecurringExpenseId)));
}

public sealed class ToggleRecurringExpenseCommandHandler(IRecurringExpenseRepository repository)
    : ICommandHandler<ToggleRecurringExpenseCommand, string>
{
    public async Task<Result<string>> HandleAsync(ToggleRecurringExpenseCommand c, CommandContext context, CancellationToken ct)
    {
        var template = await repository.FindAsync(c.RecurringExpenseId, ct);
        if (template is null || template.Deleted) return Result<string>.Fail(ErrorCodes.RecurringExpenseNotFound);
        var changed = c.IsActive ? template.Activate() : template.Deactivate();
        return changed.IsFailure ? Result<string>.Fail(changed.Error, changed.Detail) : Result<string>.Ok(template.Id);
    }
}

/// <summary>
/// Pays the template for the <b>current</b> Jalali period: creates and posts a real <see cref="Expense"/>
/// (so it flows into the ledger and the daily facts) and records the <see cref="RecurringExpensePayment"/>.
/// Idempotent: a period already paid fails with <c>RECURRING_EXPENSE_ALREADY_PAID</c>, and the unique
/// <c>(TenantId, RecurringExpenseId, PeriodCode)</c> index backstops a concurrent double-pay. Returns the expense id.
/// </summary>
[RequiresPermission(PermissionCodes.FinancePost)]
[Auditable(AuditCategory.Financial, CommandTypes.FinanceRecurringPay)]
public sealed record PayRecurringExpenseForPeriodCommand(string RecurringExpenseId,
    PaymentMethod? SettledMethod = PaymentMethod.Cash) : ICommand<string>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.FinanceRecurringPay;
    public string AggregateTypeName => nameof(RecurringExpense);
    public string? AggregateIdOrNull => RecurringExpenseId;
}

public sealed class PayRecurringExpenseForPeriodCommandValidator : IValidator<PayRecurringExpenseForPeriodCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(PayRecurringExpenseForPeriodCommand c, CancellationToken ct) =>
        ExpenseRules.Check(Rules.Required(c.RecurringExpenseId, nameof(c.RecurringExpenseId)),
            ExpenseRules.Method(c.SettledMethod, nameof(c.SettledMethod)));
}

public sealed class PayRecurringExpenseForPeriodCommandHandler(IRecurringExpenseRepository repository,
    IExpenseRepository expenses, IExpenseNumberAllocator numbers, IClock clock, IClinicTimeZoneProvider zones)
    : ICommandHandler<PayRecurringExpenseForPeriodCommand, string>
{
    public async Task<Result<string>> HandleAsync(PayRecurringExpenseForPeriodCommand c, CommandContext context, CancellationToken ct)
    {
        var template = await repository.FindAsync(c.RecurringExpenseId, ct);
        if (template is null || template.Deleted) return Result<string>.Fail(ErrorCodes.RecurringExpenseNotFound);
        if (!template.IsActive) return Result<string>.Fail(ErrorCodes.RecurringExpenseInactive);

        var period = RecurringExpensePeriods.Resolve(RecurringExpensePeriods.Today(clock, zones));
        if (await repository.FindPaymentAsync(template.Id, period.Code, ct) is not null)
            return Result<string>.Fail(ErrorCodes.RecurringExpenseAlreadyPaid);

        var now = clock.UtcNow;
        var posted = await ExpenseRules.CreateAndPostAsync(expenses, numbers, clock, context, template.CategoryCode,
            template.Title, template.Amount.Minor, now, c.SettledMethod, template.SupplierId, null, template.Note, null, ct);
        if (posted.IsFailure) return Result<string>.Fail(posted.Error, posted.Detail);

        var expenseId = posted.Value!.Id;
        repository.AddPayment(RecurringExpensePayment.Create(context.TenantId, template.Id, period.Code, expenseId, now));
        return Result<string>.Ok(expenseId);
    }
}

/// <summary>Every fixed expense with its due/paid state for the current Jalali month.</summary>
[RequiresPermission(PermissionCodes.FinanceRead)]
public sealed record RecurringExpensesQuery : IQuery<RecurringExpensesReadModel>;

public sealed class RecurringExpensesQueryHandler(IRecurringExpenseQueries queries, IClock clock, IClinicTimeZoneProvider zones)
    : IQueryHandler<RecurringExpensesQuery, RecurringExpensesReadModel>
{
    public async Task<Result<RecurringExpensesReadModel>> HandleAsync(RecurringExpensesQuery q, QueryContext context, CancellationToken ct) =>
        Result<RecurringExpensesReadModel>.Ok(await RecurringExpensePeriods.BuildAsync(queries, clock, zones, ct));
}
