using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Domain.Events;
using Animora.Persistence.Sqlite.Options;
using Animora.Persistence.Sqlite.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Animora.Persistence.Sqlite.Context;

public static class LocalStoreServiceCollectionExtensions
{
    public static IServiceCollection AddAnimoraLocalStore(this IServiceCollection services,
        Action<LocalStoreOptions>? configure = null)
    {
        services.AddOptions<LocalStoreOptions>();
        if (configure is not null) services.Configure(configure);
        if (services.Any(d => d.ServiceType == typeof(IDbContextFactory<AnimoraLocalDbContext>))) return services;

        services.AddScoped<ILocalTenantContext, SingleTenantContext>();
        services.AddSingleton<IStoreKeyProvider, NullStoreKeyProvider>();
        services.AddScoped<SqliteConnectionInterceptor>();
        // Single configuration source. Scoped factory: the interceptor and tenant come from the caller's
        // scope; background/query work creates a scope and uses IDbContextFactory for short-lived contexts.
        // AddDbContextFactory also registers AnimoraLocalDbContext itself as scoped.
        services.AddDbContextFactory<AnimoraLocalDbContext>((provider, builder) =>
        {
            var store = provider.GetRequiredService<IOptions<LocalStoreOptions>>().Value;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(store.DatabasePath))!);
            var connection = new SqliteConnectionStringBuilder { DataSource = store.DatabasePath }.ToString();
            builder.UseSqlite(connection)
                .AddInterceptors(provider.GetRequiredService<SqliteConnectionInterceptor>())
                .EnableSensitiveDataLogging(false)
                .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        }, ServiceLifetime.Scoped);

        services.AddScoped<IOwnerRepository, OwnerRepository>();
        services.AddScoped<IPetRepository, PetRepository>();
        services.AddScoped<IVisitRepository, VisitRepository>();
        services.AddScoped<IVaccinationRepository, VaccinationRepository>();
        services.AddScoped<IAppointmentRepository, AppointmentRepository>();
        services.AddScoped<IReceptionQueueRepository, ReceptionQueueRepository>();
        services.AddScoped<IBoardingRepository, BoardingRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IStockMovementRepository, StockMovementRepository>();
        services.AddScoped<IPurchasingRepository, PurchasingRepository>();
        services.AddScoped<IInvoiceRepository, InvoiceRepository>();
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<ICashSessionRepository, CashSessionRepository>();
        services.AddScoped<ILedgerRepository, LedgerRepository>();
        services.AddScoped<IChequeRepository, ChequeRepository>();
        services.AddScoped<IExpenseRepository, ExpenseRepository>();
        services.AddScoped<IPeriodRepository, PeriodRepository>();
        services.AddScoped<IIdentityRepository, IdentityRepository>();

        services.AddScoped<IUnitOfWork, LocalUnitOfWork>();
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        services.AddSingleton<DomainEventHandlerMap>();
        return services;
    }

    /// <summary>
    /// Explicitly registers a scoped domain-event handler; handlers for one event run in registration order.
    /// Duplicate (event, handler) pairs are ignored.
    /// </summary>
    public static IServiceCollection AddDomainEventHandler<TEvent, THandler>(this IServiceCollection services)
        where TEvent : DomainEvent where THandler : class, IDomainEventHandler<TEvent>
    {
        if (services.Any(d => d.ImplementationInstance is DomainEventHandlerRegistration r &&
                              r.EventType == typeof(TEvent) && r.HandlerType == typeof(THandler)))
            return services;
        services.TryAddScoped<THandler>();
        services.AddSingleton(DomainEventHandlerRegistration.For<TEvent, THandler>());
        return services;
    }
}
