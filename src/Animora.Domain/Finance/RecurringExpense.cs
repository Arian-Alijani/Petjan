using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.ValueObjects;

namespace Animora.Domain.Finance;

/// <summary>
/// A fixed (recurring) monthly cost template — rent, salaries, a subscription — that re-appears every
/// month, is reminded when due and is settled by posting a real <see cref="Expense"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Calendar-agnostic by design.</b> <see cref="DayOfMonth"/> is a plain number; reading it as a
/// Jalali day-of-month is the Application layer's job (the Domain keeps Jalali out, see
/// <see cref="AccountingPeriod"/>). The template is not a ledger row: it raises no events — paying it
/// creates and posts an ordinary expense, and a <see cref="RecurringExpensePayment"/> records which
/// period was settled.
/// </para>
/// </remarks>
public sealed class RecurringExpense : AggregateRoot
{
    /// <summary>Inclusive lower bound of <see cref="DayOfMonth"/>.</summary>
    public const int MinDayOfMonth = 1;

    /// <summary>Inclusive upper bound of <see cref="DayOfMonth"/>.</summary>
    public const int MaxDayOfMonth = 31;

    /// <summary>EF Core materialization constructor. Not for business code.</summary>
    private RecurringExpense()
    {
    }

    /// <summary>One of <see cref="ExpenseCategories"/>; resolves the debit account of each payment.</summary>
    public string CategoryCode { get; private set; } = string.Empty;

    /// <summary>What the recurring cost is, e.g. «اجاره مطب».</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>The amount posted by each payment; strictly positive.</summary>
    public Money Amount { get; private set; }

    /// <summary>Day of the (calendar) month the cost falls due, 1–31; clamped to short months by the schedule.</summary>
    public int DayOfMonth { get; private set; }

    /// <summary><see langword="true"/> while the template is reminded and payable.</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>The supplier paid, when there is one.</summary>
    public string? SupplierId { get; private set; }

    /// <summary>Free-form note copied onto every posted expense.</summary>
    public string? Note { get; private set; }

    /// <summary>When the template was defined.</summary>
    public DateTimeOffset CreatedUtc { get; private set; }

    /// <summary>Creates an active recurring expense.</summary>
    public static Result<RecurringExpense> Create(
        string tenantId,
        string categoryCode,
        string title,
        Money amount,
        int dayOfMonth,
        DateTimeOffset createdUtc,
        string? supplierId = null,
        string? note = null,
        string? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var failure = Validate(categoryCode, title, amount, dayOfMonth);
        if (failure is not null)
        {
            return Result<RecurringExpense>.Fail(failure.Value.Error, failure.Value.Detail);
        }

        return new RecurringExpense
        {
            Id = string.IsNullOrWhiteSpace(id) ? IdGenerator.NewId(createdUtc) : id,
            TenantId = tenantId,
            CategoryCode = categoryCode.Trim(),
            Title = title.Trim(),
            Amount = amount,
            DayOfMonth = dayOfMonth,
            IsActive = true,
            SupplierId = Clean(supplierId),
            Note = Clean(note),
            CreatedUtc = createdUtc,
        };
    }

    /// <summary>Edits the template. Already-posted payments are untouched; the next payment uses the new values.</summary>
    public Result Edit(
        string categoryCode,
        string title,
        Money amount,
        int dayOfMonth,
        string? supplierId = null,
        string? note = null)
    {
        var failure = Validate(categoryCode, title, amount, dayOfMonth);
        if (failure is not null)
        {
            return Result.Fail(failure.Value.Error, failure.Value.Detail);
        }

        CategoryCode = categoryCode.Trim();
        Title = title.Trim();
        Amount = amount;
        DayOfMonth = dayOfMonth;
        SupplierId = Clean(supplierId);
        Note = Clean(note);
        return Result.Ok();
    }

    /// <summary>Re-enables reminders and payment. Idempotent.</summary>
    public Result Activate()
    {
        IsActive = true;
        return Result.Ok();
    }

    /// <summary>Stops reminders and payment (e.g. the lease ended). Idempotent; history is kept.</summary>
    public Result Deactivate()
    {
        IsActive = false;
        return Result.Ok();
    }

    private static (ErrorCode Error, string Detail)? Validate(string categoryCode, string title, Money amount, int dayOfMonth)
    {
        if (string.IsNullOrWhiteSpace(categoryCode) || !ExpenseCategories.Contains(categoryCode.Trim()))
        {
            return (ErrorCodes.ExpenseCategoryInvalid, $"Unknown expense category '{categoryCode}'.");
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return (ErrorCodes.ValidationFailed, "A recurring expense needs a title.");
        }

        if (amount.Minor <= 0L)
        {
            return (ErrorCodes.ValidationFailed, $"A recurring expense must carry a strictly positive amount; got {amount.Minor}.");
        }

        if (dayOfMonth is < MinDayOfMonth or > MaxDayOfMonth)
        {
            return (ErrorCodes.ValidationFailed, $"Day of month must be {MinDayOfMonth}..{MaxDayOfMonth}; got {dayOfMonth}.");
        }

        return null;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// Records that one period of a <see cref="RecurringExpense"/> was settled by a posted
/// <see cref="Expense"/>. The unique key <c>(TenantId, RecurringExpenseId, PeriodCode)</c> is the
/// guard that makes paying a period twice impossible.
/// </summary>
public sealed class RecurringExpensePayment : SyncableEntity
{
    /// <summary>EF Core materialization constructor. Not for business code.</summary>
    private RecurringExpensePayment()
    {
    }

    /// <summary>The template that was paid.</summary>
    public string RecurringExpenseId { get; private set; } = string.Empty;

    /// <summary>
    /// Opaque period key (the Application layer uses the Jalali <c>yyyyMM</c>). The Domain never parses it —
    /// same convention as <see cref="AccountingPeriod"/>.
    /// </summary>
    public string PeriodCode { get; private set; } = string.Empty;

    /// <summary>The posted expense that settled the period.</summary>
    public string ExpenseId { get; private set; } = string.Empty;

    /// <summary>When the payment was recorded.</summary>
    public DateTimeOffset PaidUtc { get; private set; }

    /// <summary>Links a template period to the expense that settled it.</summary>
    public static RecurringExpensePayment Create(
        string tenantId,
        string recurringExpenseId,
        string periodCode,
        string expenseId,
        DateTimeOffset paidUtc,
        string? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(recurringExpenseId);
        ArgumentException.ThrowIfNullOrWhiteSpace(periodCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(expenseId);

        return new RecurringExpensePayment
        {
            Id = string.IsNullOrWhiteSpace(id) ? IdGenerator.NewId(paidUtc) : id,
            TenantId = tenantId,
            RecurringExpenseId = recurringExpenseId,
            PeriodCode = periodCode.Trim(),
            ExpenseId = expenseId,
            PaidUtc = paidUtc,
        };
    }
}

/// <summary>Where a recurring expense stands in the current period.</summary>
public enum RecurringDueState
{
    /// <summary>Settled for the current period.</summary>
    Paid = 0,

    /// <summary>Due later this period, not yet inside the reminder window.</summary>
    Upcoming = 1,

    /// <summary>Inside the reminder window before the due day.</summary>
    DueSoon = 2,

    /// <summary>Falls due today.</summary>
    Due = 3,

    /// <summary>The due day has passed and the period is still unpaid.</summary>
    Overdue = 4,

    /// <summary>The template is inactive: never reminded, never payable.</summary>
    Inactive = 5,
}

/// <summary>
/// Pure, calendar-agnostic due rules. The Application layer derives the current day-of-month, the
/// month length and the period key from "today" with the clinic calendar and passes plain ints.
/// </summary>
public static class RecurringExpenseSchedule
{
    /// <summary>Default reminder window: «سررسید نزدیک» starts this many days before the due day.</summary>
    public const int DefaultSoonWindowDays = 3;

    /// <summary>Due when the due day has been reached and the period is not yet paid.</summary>
    public static bool IsDue(int dayOfMonth, int currentDayOfMonth, bool alreadyPaidThisPeriod) =>
        currentDayOfMonth >= dayOfMonth && !alreadyPaidThisPeriod;

    /// <summary>The day the cost actually falls due this month: a day 31 in a 30-day month falls on the 30th.</summary>
    public static int EffectiveDueDay(int dayOfMonth, int daysInMonth) =>
        Math.Clamp(dayOfMonth, RecurringExpense.MinDayOfMonth, Math.Max(daysInMonth, RecurringExpense.MinDayOfMonth));

    /// <summary>Classifies a template for the current period.</summary>
    public static RecurringDueState Evaluate(
        bool isActive,
        int dayOfMonth,
        int currentDayOfMonth,
        int daysInMonth,
        bool alreadyPaidThisPeriod,
        int soonWindowDays = DefaultSoonWindowDays)
    {
        if (alreadyPaidThisPeriod)
        {
            return RecurringDueState.Paid;
        }

        if (!isActive)
        {
            return RecurringDueState.Inactive;
        }

        var due = EffectiveDueDay(dayOfMonth, daysInMonth);
        if (currentDayOfMonth > due)
        {
            return RecurringDueState.Overdue;
        }

        if (currentDayOfMonth == due)
        {
            return RecurringDueState.Due;
        }

        return due - currentDayOfMonth <= soonWindowDays ? RecurringDueState.DueSoon : RecurringDueState.Upcoming;
    }
}
