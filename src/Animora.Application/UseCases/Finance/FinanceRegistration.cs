using Animora.Application.Abstractions;
using Animora.Application.Dispatch;
using Animora.Application.UseCases.Finance.ReadModels;

namespace Animora.Application.UseCases.Finance;

/// <summary>Registers the «آمار» reads, the expense commands and the fixed-expense use-cases. Chained into <c>AddAnimoraApplication</c>.</summary>
public static class FinanceRegistration
{
    public static ApplicationRegistrationBuilder AddFinanceUseCases(this ApplicationRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            // ── Reads ─────────────────────────────────────────────────────────────
            .AddQuery<StatisticsOverviewQuery, StatisticsOverviewReadModel, StatisticsOverviewQueryHandler>()
            .AddQuery<StatisticsRevenueSeriesQuery, IReadOnlyList<StatisticsSeriesPoint>, StatisticsRevenueSeriesQueryHandler>()
            .AddQuery<StatisticsRevenueBySourceQuery, IReadOnlyList<RevenueSourceReadModel>, StatisticsRevenueBySourceQueryHandler>()
            .AddQuery<ExpenseListQuery, PagedResult<ExpenseRowReadModel>, ExpenseListQueryHandler>()
            .AddQuery<ExpenseStatsQuery, ExpenseStatsReadModel, ExpenseStatsQueryHandler>()
            .AddQuery<TopProductsQuery, IReadOnlyList<TopProductReadModel>, TopProductsQueryHandler>()
            .AddQuery<TopClientsQuery, IReadOnlyList<TopClientReadModel>, TopClientsQueryHandler>()
            .AddQuery<TopVisitorsQuery, IReadOnlyList<TopClientReadModel>, TopVisitorsQueryHandler>()
            .AddQuery<TodaysBirthdaysQuery, IReadOnlyList<BirthdayPetReadModel>, TodaysBirthdaysQueryHandler>()
            .AddQuery<SpeciesDistributionQuery, IReadOnlyList<SpeciesSliceReadModel>, SpeciesDistributionQueryHandler>()
            .AddQuery<ClientStatsQuery, ClientStatsReadModel, ClientStatsQueryHandler>()
            .AddQuery<RecurringExpensesQuery, RecurringExpensesReadModel, RecurringExpensesQueryHandler>()
            // ── Expense commands ──────────────────────────────────────────────────
            .AddCommand<AddExpenseCommand, string, AddExpenseCommandHandler>()
            .AddValidator<AddExpenseCommand, AddExpenseCommandValidator>()
            .AddCommand<ReverseExpenseCommand, string, ReverseExpenseCommandHandler>()
            .AddValidator<ReverseExpenseCommand, ReverseExpenseCommandValidator>()
            // ── Fixed (recurring) expense commands ────────────────────────────────
            .AddCommand<AddRecurringExpenseCommand, string, AddRecurringExpenseCommandHandler>()
            .AddValidator<AddRecurringExpenseCommand, AddRecurringExpenseCommandValidator>()
            .AddCommand<EditRecurringExpenseCommand, string, EditRecurringExpenseCommandHandler>()
            .AddValidator<EditRecurringExpenseCommand, EditRecurringExpenseCommandValidator>()
            .AddCommand<ToggleRecurringExpenseCommand, string, ToggleRecurringExpenseCommandHandler>()
            .AddValidator<ToggleRecurringExpenseCommand, ToggleRecurringExpenseCommandValidator>()
            .AddCommand<PayRecurringExpenseForPeriodCommand, string, PayRecurringExpenseForPeriodCommandHandler>()
            .AddValidator<PayRecurringExpenseForPeriodCommand, PayRecurringExpenseForPeriodCommandValidator>();
    }
}
