using Animora.Application.Abstractions;
using Animora.Application.Activity;
using Animora.Application.Auditing;
using Animora.Application.Commands;
using Animora.Application.Correlation;
using Animora.Application.Finance;
using Animora.Application.Outbox;
using Animora.Application.Ports;
using Animora.Application.UseCases.History;
using Animora.Application.UseCases.Dashboard;
using Animora.Application.UseCases.Profile;
using Animora.Application.UseCases.Search;
using Animora.Domain.Events;
using Animora.Persistence.Sqlite.Auditing;
using Animora.Persistence.Sqlite.Activity;
using Animora.Persistence.Sqlite.Commands;
using Animora.Persistence.Sqlite.Cashier;
using Animora.Persistence.Sqlite.Finance;
using Animora.Persistence.Sqlite.Maintenance;
using Animora.Persistence.Sqlite.Options;
using Animora.Persistence.Sqlite.Outbox;
using Animora.Persistence.Sqlite.Profile;
using Animora.Persistence.Sqlite.Queries;
using Animora.Persistence.Sqlite.Repositories;
using Animora.Persistence.Sqlite.Sales;
using Animora.Persistence.Sqlite.Security;
using Animora.Persistence.Sqlite.Seed;
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

        // Tenant comes from the cached LocalInstallation row (singleton state, no DB call in the getter).
        services.AddSingleton<LocalInstallationState>();
        services.AddScoped<ILocalTenantContext, SingleTenantContext>();
        services.AddScoped<LocalInstallationBootstrap>();
        services.TryAddSingleton<IStoreKeyProvider, NullStoreKeyProvider>();
        // Cipher seam: the real provider is reachable only when EncryptionEnabled && K2Approved (STACK.md §12 K2).
        services.AddSingleton<IStoreEncryptionProvider>(provider =>
        {
            var store = provider.GetRequiredService<IOptions<LocalStoreOptions>>().Value;
            return store is { EncryptionEnabled: true, K2Approved: true }
                ? new MultipleCiphersEncryptionProvider()
                : new NoEncryptionProvider();
        });
        // Singleton-safe: options, key provider and encryption provider are all singletons.
        services.AddSingleton<SqliteConnectionInterceptor>();
        // Single configuration source. Scoped factory: the tenant comes from the caller's scope;
        // background/query work creates a scope and uses IDbContextFactory for short-lived contexts.
        // AddDbContextFactory also registers AnimoraLocalDbContext itself as scoped.
        services.AddDbContextFactory<AnimoraLocalDbContext>((provider, builder) =>
        {
            var store = provider.GetRequiredService<IOptions<LocalStoreOptions>>().Value;
            // Fail before any open: e.g. EncryptionEnabled && !K2Approved can never reach a connection.
            var valid = store.Validate();
            if (valid.IsFailure)
                throw new InvalidOperationException($"{valid.Error.Code}: {valid.Detail ?? "invalid local store options"}");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(store.DatabasePath))!);
            var connection = new SqliteConnectionStringBuilder { DataSource = store.DatabasePath }.ToString();
            builder.UseSqlite(connection)
                .AddInterceptors(provider.GetRequiredService<SqliteConnectionInterceptor>())
                .EnableSensitiveDataLogging(false)
                .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        }, ServiceLifetime.Scoped);

        services.AddScoped<IOwnerRepository, OwnerRepository>();
        services.AddScoped<IPetRepository, PetRepository>();
        // C01: append-only weight-history writes.
        services.AddScoped<IWeightEntryRepository, WeightEntryRepository>();
        // C01: projection reads backing the Patients directory + detail screens (Application port, impl here).
        services.TryAddScoped<IPatientDirectoryQueries, PatientDirectoryQueries>();
        services.TryAddScoped<IReceptionQueueQueries, ReceptionQueueQueries>();
        services.TryAddScoped<IAppointmentQueries, AppointmentQueries>();
        services.TryAddScoped<IBoardingQueries, BoardingQueries>();
        services.TryAddScoped<IBoardingBillingHandoffWriter, BoardingBillingHandoffWriter>();
        services.TryAddScoped<ITriageQueries, TriageQueries>();
        services.TryAddScoped<IRxCatalogQueries, RxCatalogQueries>();
        services.TryAddScoped<IVaccinationQueries, VaccinationQueries>();
        // C06: snapshot-joined inventory reads + grouped purchasing KPIs (Application ports, impl here).
        services.TryAddScoped<IInventoryQueries, InventoryQueries>();
        services.TryAddScoped<IPurchasingQueries, PurchasingQueries>();
        // C07: sales read projections, transactional posting/draft facts and per-tenant invoice numbering.
        services.TryAddScoped<ISalesQueries, SalesQueries>();
        services.TryAddScoped<IServiceRateQueries, ServiceRateQueries>();
        services.TryAddScoped<ICashierQueries, CashierQueries>();
        services.TryAddScoped<IChequeQueries, ChequeQueries>();
        services.TryAddScoped<ISalesDraftData, SalesDraftData>();
        services.TryAddScoped<ISalesPostingData, SalesPostingData>();
        services.TryAddScoped<IInvoiceNumberAllocator, InvoiceNumberAllocator>();
        services.TryAddScoped<IJournalNumberAllocator, JournalNumberAllocator>();
        services.TryAddScoped<IExpenseNumberAllocator, ExpenseNumberAllocator>();
        services.TryAddScoped<IFinanceProjectionStore, FinanceProjectionStore>();
        services.TryAddScoped<IActivityFeedQueries, ActivityFeedQueries>();
        services.TryAddScoped<IDashboardQueries, DashboardQueries>();
        services.TryAddScoped<IGlobalSearchQueries, GlobalSearchQueries>();
        // C10: profile/settings reads + the non-journaled preference writer (Application ports, impl here).
        services.TryAddScoped<IProfileQueries, ProfileQueries>();
        services.TryAddScoped<IProfilePreferenceWriter, ProfilePreferenceWriter>();
        services.AddScoped<FinanceProjectionHandler>();
        services.AddScoped<IVisitRepository, VisitRepository>();
        services.AddScoped<IMedicalHistoryRepository, MedicalHistoryRepository>();
        services.AddScoped<IVisitBillingHandoffWriter, VisitBillingHandoffWriter>();
        services.AddScoped<IVaccinationRepository, VaccinationRepository>();
        services.AddScoped<IAppointmentRepository, AppointmentRepository>();
        services.AddScoped<IReceptionQueueRepository, ReceptionQueueRepository>();
        services.AddScoped<IBoardingRepository, BoardingRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IBoardingServiceRepository, BoardingServiceRepository>();
        services.AddScoped<IRoomRepository, RoomRepository>();
        services.AddScoped<IProductCategoryRepository, ProductCategoryRepository>();
        services.AddScoped<IStockMovementRepository, StockMovementRepository>();
        services.AddScoped<IPurchasingRepository, PurchasingRepository>();
        services.AddScoped<IInvoiceRepository, InvoiceRepository>();
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<ICashSessionRepository, CashSessionRepository>();
        services.TryAddScoped<ICashSessionContext, CashSessionContext>();
        services.AddScoped<ILedgerRepository, LedgerRepository>();
        services.AddScoped<IChequeRepository, ChequeRepository>();
        services.AddScoped<IExpenseRepository, ExpenseRepository>();
        services.AddScoped<IPeriodRepository, PeriodRepository>();
        services.AddScoped<IIdentityRepository, IdentityRepository>();
        services.AddScoped<IStaffMembershipRepository, StaffMembershipRepository>();
        services.AddScoped<IDeviceRepository, DeviceRepository>();
        services.AddScoped<IRoleDefinitionRepository, RoleDefinitionRepository>();
        services.AddScoped<ILoginAttemptLog, LoginAttemptLogWriter>();
        // B04b: one current signed snapshot per (tenant, user, device); joins the scoped context's UoW transaction.
        services.AddScoped<Application.Security.ICapabilityStore, CapabilityStore>();

        services.AddScoped<IUnitOfWork, LocalUnitOfWork>();
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        services.AddSingleton<DomainEventHandlerMap>();

        // Command pipeline (B02): durable journal, atomic sequence, outbox and the domain-event relay.
        // The pipeline's ambient/default dependencies are TryAdd so the local store is resolvable standalone
        // (background/query work builds a scope without the full application layer) and a real registration
        // elsewhere (AddAnimoraApplication, or E06/B05) still wins.
        services.TryAddSingleton<ICorrelationContext, CorrelationContext>();
        // The audit-backed sink surfaces integrity alerts through the presentation notification seam when a host
        // provides one. Headless hosts (manual-test data tool, background/sync services, tests) have no UI, so a
        // no-op port is registered as a fallback; a presentation host's real adapter (added later, unconditionally)
        // still wins. This keeps auditing — the security-critical part of A-RULE 019 — working in every host.
        services.TryAddSingleton<Contracts.Diagnostics.IUserNotificationPort, Contracts.Diagnostics.NullUserNotificationPort>();
        services.Replace(ServiceDescriptor.Scoped<IIntegrityAlertSink, IntegrityAlertSink>());
        services.TryAddScoped<IAuditWriter, AuditWriter>();
        services.TryAddSingleton<ICommandTransport, NullCommandTransport>();
        services.TryAddScoped<ICommandJournal, CommandJournal>();
        services.TryAddScoped<ILocalSequenceAllocator, LocalSequenceAllocator>();
        services.TryAddScoped<IOutbox, LocalOutbox>();
        services.TryAddScoped<OutboxClaim>();
        services.TryAddScoped<LocalCommandProcessor>();
        services.TryAddScoped<ISyncCursorStore, SyncCursorStore>();
        services.TryAddScoped<IUiDraftStore, UiDraftStore>();
        // Every raised domain event is relayed to the outbox (idempotent: duplicate (event, handler) pairs are ignored).
        services.AddScoped<LedgerPostingHandler>();
        services.AddDomainEventHandler<InvoicePostedEvent, LedgerPostingHandler>();
        services.AddDomainEventHandler<InvoiceVoidedEvent, LedgerPostingHandler>();
        services.AddDomainEventHandler<PaymentRegisteredEvent, LedgerPostingHandler>();
        services.AddDomainEventHandler<InvoiceRefundedEvent, LedgerPostingHandler>();
        services.AddDomainEventHandler<GoodsReceiptPostedEvent, LedgerPostingHandler>();
        services.AddDomainEventHandler<StockMovementRecordedEvent, LedgerPostingHandler>();
        services.AddDomainEventHandler<ChequeClearedEvent, LedgerPostingHandler>();
        services.AddDomainEventHandler<ChequeBouncedEvent, LedgerPostingHandler>();
        services.AddDomainEventHandler<CashSessionClosedEvent, LedgerPostingHandler>();
        services.AddDomainEventHandler<ExpensePostedEvent, LedgerPostingHandler>();
        services.AddDomainEventHandler<DomainEvent, OutboxRelayHandler>();
        services.TryAddScoped<IActivityFeedWriter, ActivityFeedWriter>();
        services.AddDomainEventHandler<DomainEvent, ActivityFeedHandler>();

        // First-run reference seeding (A10c); invoked by the startup path, never by the desktop directly.
        services.AddSingleton<ISeedPeriodCodeProvider, PersianMonthPeriodCodeProvider>();
        services.AddScoped<LocalSeeder>();
        // C01: deterministic sample owners/pets, applied behind LocalStoreOptions.SeedSampleData.
        services.AddScoped<PatientsSampleData>();
        services.AddScoped<ReceptionSampleData>();
        services.AddScoped<ClinicalSampleData>();
        services.AddScoped<SchedulingSampleData>();
        services.AddScoped<BoardingSampleData>();
        services.AddScoped<InventorySampleData>();
        services.AddScoped<SalesSampleData>();
        services.AddScoped<FinanceSampleData>();
        services.AddScoped<ClinicBakPreviewData>();

        // Read helpers (A10d): one SQL aggregate each, no tracking, tenant-filtered.
        services.TryAddScoped<StockOnHandQuery>();
        services.TryAddScoped<InvoiceBalanceQuery>();
        services.TryAddScoped<LedgerBalanceQuery>();

        // Maintenance paths (A11c); the initializer (A11d) is resolved lazily inside.
        services.TryAddScoped<ILocalStoreMaintenance, LocalStoreMaintenance>();
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
