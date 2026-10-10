using Animora.Persistence.PostgreSql.Outbox;
using Animora.Persistence.PostgreSql.Provisioning;
using Animora.Persistence.PostgreSql.Replication;
using Animora.Server.Workers;
namespace Animora.Server.Provisioning;

internal sealed class ProvisioningIntentRetentionWorker : ResilientWorker
{
    private readonly IServiceScopeFactory _scopes; private readonly IBackgroundAdmission _admission;
    public ProvisioningIntentRetentionWorker(IServiceScopeFactory scopes, IBackgroundAdmission admission, TimeProvider clock,
        WorkerHeartbeat heartbeat, ILogger<ProvisioningIntentRetentionWorker> logger)
        : base("provisioning-retention", TimeSpan.FromDays(1), clock, heartbeat, logger) { _scopes = scopes; _admission = admission; }
    protected override async Task TickAsync(CancellationToken ct)
    {
        await using var permit = await _admission.EnterAsync(ct).ConfigureAwait(false); await using var scope = _scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<WorkerExecutionContext>().IsWorker = true;
        await scope.ServiceProvider.GetRequiredService<IProvisioningIntentStore>().RetainAsync(Clock.GetUtcNow(), ct).ConfigureAwait(false);
    }
}
