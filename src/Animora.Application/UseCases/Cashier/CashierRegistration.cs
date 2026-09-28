using Animora.Application.Abstractions;
using Animora.Application.Dispatch;
using Animora.Application.Ports;
using Animora.Application.UseCases.Sales.ReadModels;

namespace Animora.Application.UseCases.Cashier;

public static class CashierRegistration
{
    public static ApplicationRegistrationBuilder AddCashierUseCases(this ApplicationRegistrationBuilder builder) => builder
        .AddQuery<CashierInvoiceListQuery, PagedResult<InvoiceSummaryReadModel>, CashierInvoiceListQueryHandler>()
        .AddQuery<CashierStatsQuery, CashierStatsReadModel, CashierStatsQueryHandler>()
        .AddQuery<CashierTabCountsQuery, CashierTabCountsReadModel, CashierTabCountsQueryHandler>()
        .AddQuery<OpenCashSessionQuery, OpenCashSessionReadModel?, OpenCashSessionQueryHandler>()
        .AddCommand<OpenCashSessionCommand, string, OpenCashSessionCommandHandler>()
        .AddCommand<CloseCashSessionCommand, Unit, CloseCashSessionCommandHandler>()
        .AddCommand<RecordCashMovementCommand, Unit, RecordCashMovementCommandHandler>();
}
