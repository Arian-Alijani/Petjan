using Animora.Application.UseCases.Finance;
using Animora.Application.UseCases.Finance.ReadModels;
using Animora.Contracts.Statistics;
using Animora.Domain.Finance;
using Animora.Domain.Sales;
using Dto = Animora.Contracts.Statistics;

namespace Animora.Client.Services.Statistics;

/// <summary>Pure Application read model ⇄ Contracts DTO mapping (1:1 with the Task 2/3 dispatcher shapes).</summary>
internal static class StatisticsMapping
{
    public static StatisticsWindow ToWindow(this StatisticsRange range) =>
        new(range.RangeId, range.CustomFromDay, range.CustomToDay);

    public static StatisticsTotals ToContract(this FinanceTotalsReadModel t) =>
        new(t.GrossRevenueMinor, t.DiscountMinor, t.RefundMinor, t.CogsMinor, t.ExpenseMinor, t.InvoiceCount,
            t.PaidInvoiceCount, t.VisitCount);

    public static StatisticsOverview ToContract(this StatisticsOverviewReadModel o) =>
        new(o.Range.FromDay, o.Range.ToDay, o.PreviousRange.FromDay, o.PreviousRange.ToDay,
            o.Current.ToContract(), o.Previous.ToContract(), o.Today.ToContract());

    public static Dto.StatisticsSeriesPoint ToContract(this Animora.Application.UseCases.Finance.ReadModels.StatisticsSeriesPoint p) =>
        new(p.Start, p.RevenueMinor, p.ExpenseMinor, p.CogsMinor, p.InvoiceCount, p.VisitCount);

    public static RevenueSourceSlice ToContract(this RevenueSourceReadModel s) => new(s.Source, s.AmountMinor, s.LineCount);

    public static TopProduct ToContract(this TopProductReadModel p) => new(p.ProductId, p.Name, p.Quantity, p.RevenueMinor, p.CogsMinor);

    public static TopClient ToContract(this TopClientReadModel c) =>
        new(c.OwnerId, c.FullName, c.Phone, c.TotalSpendMinor, c.InvoiceCount, c.LastInvoiceUtc);

    public static SpeciesSlice ToContract(this SpeciesSliceReadModel s) => new(s.Species, s.Count);

    public static ClientStats ToContract(this ClientStatsReadModel c) =>
        new(c.NewOwners, c.ActiveOwners, c.ReturningOwners, c.TotalOwners);

    public static ExpenseRow ToContract(this ExpenseRowReadModel r) =>
        new(r.Id, r.Number, r.CategoryCode, r.Title, r.AmountMinor, r.IncurredUtc,
            r.State == ExpenseState.Reversed ? ExpenseStatus.Reversed : ExpenseStatus.Posted,
            r.SettledMethod.ToContract(), r.SupplierName, r.Note, r.ReversesExpenseId);

    public static ExpenseCategoryStat ToContract(this ExpenseCategoryTotalReadModel c) =>
        new(c.CategoryCode, c.AmountMinor, c.PreviousAmountMinor, c.Count);

    public static ExpenseStats ToContract(this ExpenseStatsReadModel s) =>
        new(s.TotalMinor, s.PreviousTotalMinor, s.Categories.Select(c => c.ToContract()).ToArray(),
            s.Trend.Select(p => p.ToContract()).ToArray());

    public static RecurringExpenseItem ToContract(this RecurringExpenseReadModel r) =>
        new(r.Id, r.CategoryCode, r.Title, r.AmountMinor, r.DayOfMonth, r.IsActive, r.SupplierId, r.SupplierName, r.Note,
            r.State.ToContract(), r.DueDay, r.DaysUntilDue, r.PaidExpenseId, r.PaidUtc);

    public static RecurringExpenseList ToContract(this RecurringExpensesReadModel r) =>
        new(r.PeriodCode, r.Today, r.Items.Select(i => i.ToContract()).ToArray());

    public static RecurringStatus ToContract(this RecurringDueState state) => state switch
    {
        RecurringDueState.Paid => RecurringStatus.Paid,
        RecurringDueState.DueSoon => RecurringStatus.DueSoon,
        RecurringDueState.Due => RecurringStatus.Due,
        RecurringDueState.Overdue => RecurringStatus.Overdue,
        RecurringDueState.Inactive => RecurringStatus.Inactive,
        _ => RecurringStatus.Upcoming,
    };

    /// <summary>An expense without a settlement method is still payable (credit → accounts payable).</summary>
    public static ExpenseSettlement ToContract(this PaymentMethod? method) => method switch
    {
        PaymentMethod.Card => ExpenseSettlement.Card,
        PaymentMethod.Transfer => ExpenseSettlement.Transfer,
        PaymentMethod.Cash => ExpenseSettlement.Cash,
        _ => ExpenseSettlement.Credit,
    };

    /// <summary><c>Credit</c> leaves the expense unsettled (null) so posting credits accounts payable.</summary>
    public static PaymentMethod? ToDomain(this ExpenseSettlement settlement) => settlement switch
    {
        ExpenseSettlement.Card => PaymentMethod.Card,
        ExpenseSettlement.Transfer => PaymentMethod.Transfer,
        ExpenseSettlement.Credit => null,
        _ => PaymentMethod.Cash,
    };
}
