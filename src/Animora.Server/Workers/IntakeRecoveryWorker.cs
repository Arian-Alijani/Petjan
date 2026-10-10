using Animora.Persistence.PostgreSql.Commands;
using Animora.Persistence.PostgreSql.Identity;
using Animora.Persistence.PostgreSql.Outbox;
using Animora.Persistence.PostgreSql.Replication;
using Animora.Server.Commands;
namespace Animora.Server.Workers;

internal sealed class IntakeRecoveryWorker : ResilientWorker
{
    private readonly IServiceScopeFactory _scopes;
    private readonly WorkerTenantDirectory _tenants;
    private readonly IBackgroundAdmission _admission;
    public IntakeRecoveryWorker(IServiceScopeFactory scopes, WorkerTenantDirectory tenants, IBackgroundAdmission admission,
        TimeProvider clock, WorkerHeartbeat heartbeat, ILogger<IntakeRecoveryWorker> logger)
        : base("intake-recovery", TimeSpan.FromSeconds(30), clock, heartbeat, logger)
    { _scopes = scopes; _tenants = tenants; _admission = admission; }
    protected override async Task TickAsync(CancellationToken ct)
    {
        await using var lease = await _admission.EnterAsync(ct).ConfigureAwait(false);
        int remaining = 50;
        await foreach (var tenant in _tenants.ReadAsync(ct).ConfigureAwait(false))
        {
            await using var scope = _scopes.CreateAsyncScope(); var sp = scope.ServiceProvider;
            sp.GetRequiredService<WorkerExecutionContext>().IsWorker = true;
            sp.GetRequiredService<AuthTenantContext>().Bind(tenant);
            var store = sp.GetRequiredService<ICommandIntakeStore>();
            var rows = await store.ListExpiredLeasesAsync(Clock.GetUtcNow(), remaining, ct).ConfigureAwait(false);
            foreach (var row in rows)
            {
                var result = await sp.GetRequiredService<IntakeRecoveryService>().RecoverAsync(row, ct).ConfigureAwait(false);
                if (result.IsSuccess)
                    // S06 records intentionally omit auth identity/aggregate concurrency evidence.
                    // Never invent authority to replay money effects. Release as fenced Failed so
                    // an authenticated retry can safely replay with current policies and envelope.
                    await store.MarkFailedAsync(row.OperationId, result.Value!, "awaiting_authenticated_replay", ct).ConfigureAwait(false);
                if (--remaining == 0) return;
            }
        }
    }
}
