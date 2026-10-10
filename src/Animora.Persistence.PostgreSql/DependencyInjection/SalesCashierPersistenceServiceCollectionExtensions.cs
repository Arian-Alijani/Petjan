using Animora.Application.Commands;
using Animora.Application.Ports;
using Animora.Persistence.PostgreSql.Commands;
using Animora.Persistence.PostgreSql.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Animora.Persistence.PostgreSql.DependencyInjection;

public static class SalesCashierPersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddAnimoraSalesCashierPersistence(this IServiceCollection services)
    {
        services.TryAddScoped<IInvoiceRepository, InvoiceRepository>();
        services.TryAddScoped<IPaymentRepository, PaymentRepository>();
        services.TryAddScoped<IInsurerRepository, InsurerRepository>();
        services.TryAddScoped<ICashSessionRepository, CashSessionRepository>();
        services.TryAddScoped<ISalesDraftData, SalesDraftData>();
        services.TryAddScoped<ISalesPostingData, SalesPostingData>();
        services.TryAddScoped<ISalesQueries, SalesQueries>();
        services.TryAddScoped<IServiceRateQueries, ServiceRateQueries>();
        services.TryAddScoped<IInvoiceNumberAllocator, InvoiceNumberAllocator>();
        services.TryAddScoped<ILocalSequenceAllocator, LocalSequenceAllocator>();
        services.TryAddScoped<ICashSessionContext, CashSessionContext>();
        services.TryAddScoped<ICashierQueries, CashierQueries>();
        services.TryAddScoped<DocumentCounterAllocator>();
        services.TryAddScoped<ServerCommandExecutionScope>();
        return services;
    }
}
