using Animora.Persistence.PostgreSql.Identity;
using Animora.Persistence.PostgreSql.Outbox;
using Animora.Persistence.PostgreSql.Replication;
namespace Animora.Server.Workers;

internal sealed class FeedRetentionWorker : ResilientWorker
{
    private readonly IServiceScopeFactory _scopes; private readonly WorkerTenantDirectory _tenants;
    public FeedRetentionWorker(IServiceScopeFactory scopes, WorkerTenantDirectory tenants, TimeProvider clock,
        WorkerHeartbeat heartbeat, ILogger<FeedRetentionWorker> logger)
        : base("feed-retention", TimeSpan.FromHours(1), clock, heartbeat, logger) { _scopes = scopes; _tenants = tenants; }
    protected override async Task TickAsync(CancellationToken ct)
    {
        await foreach (var tenant in _tenants.ReadAsync(ct).ConfigureAwait(false))
        {
            await using var scope = _scopes.CreateAsyncScope(); var sp = scope.ServiceProvider;
            sp.GetRequiredService<WorkerExecutionContext>().IsWorker = true;
            sp.GetRequiredService<AuthTenantContext>().Bind(tenant);
            // RunAsync takes the shared background admission itself; never double-acquire it.
            await sp.GetRequiredService<FeedRetentionService>().RunAsync(ct).ConfigureAwait(false);
        }
    }
}
