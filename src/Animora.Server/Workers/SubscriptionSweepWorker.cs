using Animora.Persistence.PostgreSql.Outbox;
using Animora.Persistence.PostgreSql.Replication;
using Animora.Persistence.PostgreSql.Subscriptions;

namespace Animora.Server.Workers;

internal sealed class SubscriptionSweepWorker : ResilientWorker
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IBackgroundAdmission _admission;
    public SubscriptionSweepWorker(IServiceScopeFactory scopes, IBackgroundAdmission admission, TimeProvider clock,
        WorkerHeartbeat heartbeat, ILogger<SubscriptionSweepWorker> logger)
        : base("subscription-sweep", TimeSpan.FromMinutes(15), clock, heartbeat, logger)
    { _scopes = scopes; _admission = admission; }
    protected override async Task TickAsync(CancellationToken ct)
    {
        await using var permit = await _admission.EnterAsync(ct).ConfigureAwait(false);
        await using var scope = _scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<WorkerExecutionContext>().IsWorker = true;
        await scope.ServiceProvider.GetRequiredService<SubscriptionSweep>().EnqueueDueAsync(ct).ConfigureAwait(false);
    }
}
