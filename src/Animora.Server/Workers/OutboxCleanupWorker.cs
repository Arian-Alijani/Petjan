using Animora.Persistence.PostgreSql.Outbox;
using Animora.Persistence.PostgreSql.Replication;
namespace Animora.Server.Workers;

internal sealed class OutboxCleanupWorker : ResilientWorker
{
    private readonly IServiceScopeFactory _scopes; private readonly IBackgroundAdmission _admission;
    public OutboxCleanupWorker(IServiceScopeFactory scopes, IBackgroundAdmission admission, TimeProvider clock,
        WorkerHeartbeat heartbeat, ILogger<OutboxCleanupWorker> logger)
        : base("outbox-cleanup", TimeSpan.FromDays(1), clock, heartbeat, logger) { _scopes = scopes; _admission = admission; }
    protected override async Task TickAsync(CancellationToken ct)
    {
        await using var lease = await _admission.EnterAsync(ct).ConfigureAwait(false);
        await using var scope = _scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<OutboxClaimStore>()
            .CleanupAsync(Clock.GetUtcNow().AddDays(-7), ct).ConfigureAwait(false);
    }
}
