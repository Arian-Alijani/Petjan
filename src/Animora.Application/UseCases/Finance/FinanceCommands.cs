using Animora.Application.Abstractions;
using Animora.Application.Auditing;
using Animora.Application.Commands;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Finance;
using Animora.Domain.Identity;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Sales;
using Animora.Domain.Time;
using Animora.Domain.ValueObjects;

namespace Animora.Application.UseCases.Finance;

/// <summary>
/// Records a clinic operating cost. The command creates a draft <see cref="Expense"/>, allocates its
/// tenant/year-scoped number and posts it; posting raises <c>ExpensePostedEvent</c>, which the
/// single-writer <c>LedgerPostingHandler</c> turns into the journal entry inside the same unit of
/// work. This command never writes the ledger directly (single-writer invariant, A-RULE).
/// </summary>
[RequiresPermission(PermissionCodes.FinancePost)]
[Auditable(AuditCategory.Financial, CommandTypes.FinanceExpensePost)]
public sealed record RecordExpenseCommand(
    string CategoryCode,
    string Title,
    long AmountMinor,
    DateTimeOffset IncurredUtc,
    PaymentMethod? SettledMethod,
    string? Note = null)
    : ICommand<string>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.FinanceExpensePost;
    public string AggregateTypeName => "Expense";
    public string? AggregateIdOrNull => null;
}

public sealed class RecordExpenseCommandHandler(
    IExpenseRepository expenses,
    IExpenseNumberAllocator numbers,
    IClock clock)
    : ICommandHandler<RecordExpenseCommand, string>
{
    public async Task<Result<string>> HandleAsync(RecordExpenseCommand c, CommandContext context, CancellationToken ct)
    {
        if (!ExpenseCategories.Contains(c.CategoryCode))
            return Result<string>.Fail(ErrorCodes.ExpenseCategoryInvalid, $"Unknown expense category '{c.CategoryCode}'.");

        var number = await numbers.AllocateAsync(context.TenantId, c.IncurredUtc, ct).ConfigureAwait(false);

        var created = Expense.Create(
            context.TenantId, number, c.CategoryCode, c.Title, Money.Irt(c.AmountMinor),
            c.IncurredUtc, context.UserId, c.SettledMethod, note: c.Note);
        if (created.IsFailure) return Result<string>.Fail(created.Error, created.Detail);

        var posted = created.Value!.Post(clock);
        if (posted.IsFailure) return Result<string>.Fail(posted.Error, posted.Detail);

        expenses.Add(created.Value!);
        return Result<string>.Ok(created.Value!.Id);
    }
}

/// <summary>
/// Closes an accounting period. There is deliberately no reopen: the Domain state machine is strictly
/// forward (Open → Closing → Closed → Locked) and a correction after the close is a reversing entry in
/// the next open period (A-RULE 002/003). The close blockers are all supplied as <c>false</c>: no
/// count seam is available for unsettled commands / open cash sessions / draft invoices at the
/// Application layer here, so this is a simple close. The mandatory <c>closingEntryId</c> is generated
/// as a reference id (<see cref="IdGenerator.NewId(DateTimeOffset)"/>) — no physical journal entry is
/// built, because that is the single writer's job and the facts-only finance queries do not depend on
/// a closing entry.
/// </summary>
[RequiresPermission(PermissionCodes.FinancePeriodClose)]
[Auditable(AuditCategory.Financial, CommandTypes.FinancePeriodClose)]
public sealed record CloseAccountingPeriodCommand(string PeriodCode)
    : ICommand<Unit>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.FinancePeriodClose;
    public string AggregateTypeName => "AccountingPeriod";
    public string? AggregateIdOrNull => null;
}

public sealed class CloseAccountingPeriodCommandHandler(IPeriodRepository periods, IClock clock)
    : ICommandHandler<CloseAccountingPeriodCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(CloseAccountingPeriodCommand c, CommandContext context, CancellationToken ct)
    {
        var period = await periods.FindByCodeAsync(c.PeriodCode, ct).ConfigureAwait(false);
        if (period is null)
            return Result<Unit>.Fail(ErrorCodes.PeriodNotFound, $"No period '{c.PeriodCode}'.");

        // Close blockers: no count seam is exposed at this layer, so a simple close is performed.
        var begin = period.BeginClose();
        if (begin.IsFailure) return Result<Unit>.Fail(begin.Error, begin.Detail);

        // Reference-only closing entry id; a physical entry is left to the single writer and is not
        // required by the facts-only finance queries.
        var closingEntryId = IdGenerator.NewId(clock.UtcNow);
        var closed = period.Close(false, false, false, clock, closingEntryId, context.UserId, note: null);
        return closed.IsFailure ? Result<Unit>.Fail(closed.Error, closed.Detail) : Result<Unit>.Ok(Unit.Value);
    }
}
