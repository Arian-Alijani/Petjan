using Animora.Application.Ports;
using Animora.Persistence.PostgreSql.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace Animora.Persistence.PostgreSql.DependencyInjection;
public static class MoneyStockPersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddAnimoraMoneyStockPersistence(this IServiceCollection services)
    {
        services.TryAddScoped<IProductRepository, ProductRepository>();
        services.TryAddScoped<IProductCategoryRepository, ProductCategoryRepository>();
        services.TryAddScoped<IInventoryQueries, InventoryQueries>();
        services.TryAddScoped<IPurchasingRepository, PurchasingRepository>();
        services.TryAddScoped<IPurchasingQueries, PurchasingQueries>();
        services.TryAddScoped<ISupplierRepository, SupplierRepository>();
        services.TryAddScoped<ILedgerRepository, LedgerRepository>();
        services.TryAddScoped<IPeriodRepository, PeriodRepository>();
        services.TryAddScoped<IJournalNumberAllocator, JournalNumberAllocator>();
        services.TryAddScoped<IChequeRepository, ChequeRepository>();
        services.TryAddScoped<IExpenseRepository, ExpenseRepository>();
        services.TryAddScoped<IExpenseNumberAllocator, ExpenseNumberAllocator>();
        services.TryAddScoped<IRecurringExpenseRepository, RecurringExpenseRepository>();
        services.Replace(ServiceDescriptor.Scoped<IStockMovementRepository, StockMovementRepository>());
        return services;
    }
}
