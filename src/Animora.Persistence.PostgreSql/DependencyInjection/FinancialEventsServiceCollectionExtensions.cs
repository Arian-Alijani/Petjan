using Animora.Application.Abstractions;
using Animora.Application.Finance;
using Animora.Application.Ports;
using Animora.Domain.Events;
using Animora.Persistence.PostgreSql.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace Animora.Persistence.PostgreSql.DependencyInjection;
public static class FinancialEventsServiceCollectionExtensions
{
    public static IServiceCollection AddAnimoraServerFinancialEvents(this IServiceCollection services)
    {
        // On-demand accounting-period provisioning: when a financial event falls in a month the
        // clinic never opened, the ledger auto-opens that Jalali month rather than refusing the post
        // (PERIOD_NOT_FOUND). Closing a period remains a deliberate operator action.
        services.TryAddScoped<IAccountingPeriodBoundaryResolver, JalaliMonthPeriodBoundaryResolver>();
        services.TryAddScoped<LedgerPostingHandler>();
        services.Replace(ServiceDescriptor.Scoped<IDomainEventDispatcher, ServerDomainEventDispatcher>());
        services.TryAddScoped<IServerFinancialEventProcessor, ServerFinancialEventProcessor>();
        services.AddScoped<IDomainEventHandler<InvoicePostedEvent>>(sp => sp.GetRequiredService<LedgerPostingHandler>());
        services.AddScoped<IDomainEventHandler<InvoiceVoidedEvent>>(sp => sp.GetRequiredService<LedgerPostingHandler>());
        services.AddScoped<IDomainEventHandler<PaymentRegisteredEvent>>(sp => sp.GetRequiredService<LedgerPostingHandler>());
        services.AddScoped<IDomainEventHandler<InvoiceRefundedEvent>>(sp => sp.GetRequiredService<LedgerPostingHandler>());
        services.AddScoped<IDomainEventHandler<GoodsReceiptPostedEvent>>(sp => sp.GetRequiredService<LedgerPostingHandler>());
        services.AddScoped<IDomainEventHandler<StockMovementRecordedEvent>>(sp => sp.GetRequiredService<LedgerPostingHandler>());
        services.AddScoped<IDomainEventHandler<ChequeClearedEvent>>(sp => sp.GetRequiredService<LedgerPostingHandler>());
        services.AddScoped<IDomainEventHandler<ChequeBouncedEvent>>(sp => sp.GetRequiredService<LedgerPostingHandler>());
        services.AddScoped<IDomainEventHandler<CashSessionClosedEvent>>(sp => sp.GetRequiredService<LedgerPostingHandler>());
        services.AddScoped<IDomainEventHandler<ExpensePostedEvent>>(sp => sp.GetRequiredService<LedgerPostingHandler>());
        return services;
    }
}
