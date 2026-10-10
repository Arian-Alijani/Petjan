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
        // Same process-wide admission instance as S12 (TryAdd preserves that registration).
        services.TryAddSingleton<IBackgroundAdmission>(_ => new SemaphoreBackgroundAdmission(1));
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
