using Animora.Domain.Finance;

namespace Animora.Application.UseCases.Finance.ReadModels;

/// <summary>One fixed expense with its state in the current (Jalali) month.</summary>
/// <param name="DueDay">Gregorian date the cost falls due this period (day clamped to the month length).</param>
/// <param name="DaysUntilDue">Days from today to <paramref name="DueDay"/>; negative once overdue.</param>
/// <param name="PaidExpenseId">The posted expense that settled this period; <see langword="null"/> while unpaid.</param>
public sealed record RecurringExpenseReadModel(string Id, string CategoryCode, string Title, long AmountMinor,
    int DayOfMonth, bool IsActive, string? SupplierId, string? SupplierName, string? Note,
    RecurringDueState State, DateOnly DueDay, int DaysUntilDue, string? PaidExpenseId, DateTimeOffset? PaidUtc)
{
    /// <summary>The «پرداخت» button applies: active, unpaid, and the due day is reached or near.</summary>
    public bool CanPay => State is RecurringDueState.Upcoming or RecurringDueState.DueSoon
        or RecurringDueState.Due or RecurringDueState.Overdue;

    /// <summary>Needs the manager's attention now (due soon, due today or overdue).</summary>
    public bool NeedsAttention => State is RecurringDueState.DueSoon or RecurringDueState.Due or RecurringDueState.Overdue;
}

/// <summary>All fixed expenses for the current period; <paramref name="PeriodCode"/> is the Jalali <c>yyyyMM</c> key.</summary>
public sealed record RecurringExpensesReadModel(string PeriodCode, DateOnly Today, IReadOnlyList<RecurringExpenseReadModel> Items);
