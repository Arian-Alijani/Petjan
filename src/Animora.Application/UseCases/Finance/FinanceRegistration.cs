using Animora.Application.Abstractions;
using Animora.Application.Dispatch;
using Animora.Application.UseCases.Finance.ReadModels;
using Animora.Domain.Finance;
using Animora.Domain.Sales;

namespace Animora.Application.UseCases.Finance;

/// <summary>Registers the C09 finance reporting queries and the two finance commands.</summary>
public static class FinanceRegistration
{
    public static ApplicationRegistrationBuilder AddFinanceUseCases(this ApplicationRegistrationBuilder builder) => builder
        .AddQuery<FinanceOverviewQuery, FinanceOverviewReadModel, FinanceOverviewQueryHandler>()
        .AddQuery<FinanceIncomeQuery, FinanceIncomeReadModel, FinanceIncomeQueryHandler>()
        .AddQuery<FinanceExpenseQuery, FinanceExpenseReadModel, FinanceExpenseQueryHandler>()
        .AddQuery<FinanceProfitQuery, FinanceProfitReadModel, FinanceProfitQueryHandler>()
        .AddQuery<FinanceRangeCatalogQuery, IReadOnlyList<FinanceRangeDefinition>, FinanceRangeCatalogQueryHandler>()
        .AddQuery<FinanceExpenseCategoriesQuery, IReadOnlyList<string>, FinanceExpenseCategoriesQueryHandler>()
        .AddQuery<FinancePaymentMethodsQuery, IReadOnlyList<PaymentMethod>, FinancePaymentMethodsQueryHandler>()
        .AddCommand<RecordExpenseCommand, string, RecordExpenseCommandHandler>()
        .AddCommand<CloseAccountingPeriodCommand, Unit, CloseAccountingPeriodCommandHandler>();
}
