using System;
using System.Collections.Generic;

namespace Animora.Contracts.Statistics;

/// <summary>
/// The requested «آمار» window. <see cref="RangeId"/> is one of <c>today</c>, <c>7d</c>, <c>30d</c>, <c>90d</c>,
/// <c>ytd</c> (from 1 Farvardin) or <c>custom</c>; the custom bounds are Gregorian <c>yyyyMMdd</c> clinic-local days.
/// </summary>
public sealed record StatisticsRange(string RangeId = "30d", int? CustomFromDay = null, int? CustomToDay = null);

/// <summary>Finance totals of one window, in minor units (the currency's smallest unit as stored).</summary>
public sealed record StatisticsTotals(long GrossRevenueMinor, long DiscountMinor, long RefundMinor, long CogsMinor,
    long ExpenseMinor, int InvoiceCount, int PaidInvoiceCount, int VisitCount)
{
    /// <summary>Net revenue: gross − discount − refunds.</summary>
    public long RevenueMinor => GrossRevenueMinor - DiscountMinor - RefundMinor;

    /// <summary>Net profit «سود خالص»: revenue − COGS − expenses.</summary>
    public long NetMinor => RevenueMinor - CogsMinor - ExpenseMinor;

    /// <summary>Average net revenue per invoice (0 when none).</summary>
    public long AverageInvoiceMinor => InvoiceCount <= 0 ? 0 : RevenueMinor / InvoiceCount;
}

/// <summary>Window, previous comparable window, and totals of each plus today (for the KPI deltas).</summary>
public sealed record StatisticsOverview(int FromDay, int ToDay, int PreviousFromDay, int PreviousToDay,
    StatisticsTotals Current, StatisticsTotals Previous, StatisticsTotals Today);

/// <summary>One chart bucket (day / week / Jalali month) starting on <paramref name="Start"/>.</summary>
public sealed record StatisticsSeriesPoint(DateOnly Start, long RevenueMinor, long ExpenseMinor, long CogsMinor,
    int InvoiceCount, int VisitCount);

/// <summary>What the revenue was earned from: <c>visit</c>, <c>product</c>, <c>boarding</c> or <c>other</c>.</summary>
public sealed record RevenueSourceSlice(string Source, long AmountMinor, int LineCount);

/// <summary>Lifecycle of an expense row in the ledger.</summary>
public enum ExpenseStatus { Posted, Reversed }

/// <summary>How an expense is (or will be) settled.</summary>
public enum ExpenseSettlement { Cash, Card, Transfer, Credit }

/// <summary>One expense ledger row. <see cref="ReversesExpenseId"/> is set on a correcting (reversing) row.</summary>
public sealed record ExpenseRow(string Id, string Number, string CategoryCode, string Title, long AmountMinor,
    DateTimeOffset IncurredUtc, ExpenseStatus Status, ExpenseSettlement Settlement, string? SupplierName, string? Note,
    string? ReversesExpenseId)
{
    /// <summary>A posted, original (non-reversal) row can still be corrected by a reversing expense.</summary>
    public bool CanReverse => Status == ExpenseStatus.Posted && ReversesExpenseId is null;
}

/// <summary>One page of the expense ledger (newest first).</summary>
public sealed record ExpensePage(IReadOnlyList<ExpenseRow> Items, int TotalCount, int Page, int PageSize)
{
    /// <summary>True when more rows exist beyond this page.</summary>
    public bool HasMore => (long)Page * PageSize < TotalCount;
}

/// <summary>One category's net spend in the window and in the previous comparable window.</summary>
public sealed record ExpenseCategoryStat(string CategoryCode, long AmountMinor, long PreviousAmountMinor, int Count);

/// <summary>Expense analytics of a window: totals, per-category breakdown and per-bucket trend.</summary>
public sealed record ExpenseStats(long TotalMinor, long PreviousTotalMinor, IReadOnlyList<ExpenseCategoryStat> Categories,
    IReadOnlyList<StatisticsSeriesPoint> Trend);

/// <summary>A best-selling product of the window.</summary>
public sealed record TopProduct(string ProductId, string Name, decimal Quantity, long RevenueMinor, long CogsMinor);

/// <summary>A client ranked by lifetime spend.</summary>
public sealed record TopClient(string OwnerId, string FullName, string Phone, long TotalSpendMinor, int InvoiceCount,
    DateTimeOffset? LastInvoiceUtc);

/// <summary>Active patients of one species.</summary>
public sealed record SpeciesSlice(string Species, int Count);

/// <summary>Client mix of the window: new registrations and the new/returning split of paying clients.</summary>
public sealed record ClientStats(int NewOwners, int ActiveOwners, int ReturningOwners, int TotalOwners);

/// <summary>Where a fixed expense stands in the current period.</summary>
public enum RecurringStatus { Paid, Upcoming, DueSoon, Due, Overdue, Inactive }

/// <summary>
/// One fixed (monthly) expense with its current-period state. <see cref="DueDay"/> is the Gregorian date the cost
/// falls due this period; render it with the Jalali helpers.
/// </summary>
public sealed record RecurringExpenseItem(string Id, string CategoryCode, string Title, long AmountMinor, int DayOfMonth,
    bool IsActive, string? SupplierId, string? SupplierName, string? Note, RecurringStatus Status, DateOnly DueDay,
    int DaysUntilDue, string? PaidExpenseId, DateTimeOffset? PaidUtc)
{
    /// <summary>The «پرداخت» button applies.</summary>
    public bool CanPay => Status is RecurringStatus.Upcoming or RecurringStatus.DueSoon or RecurringStatus.Due
        or RecurringStatus.Overdue;

    /// <summary>Due soon, due today or overdue — worth the manager's attention.</summary>
    public bool NeedsAttention => Status is RecurringStatus.DueSoon or RecurringStatus.Due or RecurringStatus.Overdue;
}

/// <summary>All fixed expenses for the current Jalali period (<see cref="PeriodCode"/> = Jalali <c>yyyyMM</c>).</summary>
public sealed record RecurringExpenseList(string PeriodCode, DateOnly Today, IReadOnlyList<RecurringExpenseItem> Items);

/// <summary>Input of a one-off expense (posted immediately).</summary>
public sealed record NewExpense(string CategoryCode, string Title, long AmountMinor, DateTimeOffset IncurredUtc,
    ExpenseSettlement Settlement = ExpenseSettlement.Cash, string? SupplierId = null, string? ReceiptFileId = null,
    string? Note = null);

/// <summary>Input of a fixed-expense template (add or edit).</summary>
public sealed record RecurringExpenseInput(string CategoryCode, string Title, long AmountMinor, int DayOfMonth,
    string? SupplierId = null, string? Note = null);
