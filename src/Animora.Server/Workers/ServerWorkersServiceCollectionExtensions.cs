using Animora.Persistence.PostgreSql.Outbox;
using Animora.Persistence.PostgreSql.Replication;
using Animora.Server.Hints;
using Animora.Server.Hosting;
using Animora.Server.Resilience;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace Animora.Server.Workers;

internal static class ServerWorkersServiceCollectionExtensions
{
    public static IServiceCollection AddAnimoraWorkers(this IServiceCollection services)
    {
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        // SCALE-ADM-01: the authoritative, size-aware registrations for both admission gates live
        // in AddAnimoraPostgreSql (worker gate = IBackgroundAdmission, request-path sync gate =
        // ISyncAdmission). These TryAdd fallbacks only fire if persistence was not wired, keeping
        // the two gates distinct so background workers never share a permit with the sync build.
        services.TryAddSingleton<IBackgroundAdmission>(_ => new SemaphoreBackgroundAdmission(1));
        services.TryAddSingleton<ISyncAdmission>(_ => new SemaphoreSyncAdmission(2));
        services.TryAddSingleton<WorkerHeartbeat>(); services.TryAddSingleton<CircuitBreaker>();
        services.TryAddSingleton<OutboundTimeout>();
        services.AddScoped<IOutboxConsumer, FeedAdvancedHintConsumer>();
        services.AddScoped<IOutboxConsumer, Animora.Server.Subscriptions.SubscriptionExpiryConsumer>();
        services.AddHostedService<SubscriptionSweepWorker>();
        services.AddSingleton<IReadinessProbe, WorkersReadinessProbe>();
        services.AddSignalR();
        services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(10));
        services.AddHostedService<OutboxRelayWorker>(); services.AddHostedService<IntakeRecoveryWorker>();
        services.AddHostedService<CommandIntakeRetentionWorker>(); services.AddHostedService<FeedRetentionWorker>();
        services.AddHostedService<OutboxCleanupWorker>();
        return services;
    }
}
