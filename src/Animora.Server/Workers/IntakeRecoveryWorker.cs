using Animora.Persistence.PostgreSql.Commands;
using Animora.Persistence.PostgreSql.Identity;
using Animora.Persistence.PostgreSql.Outbox;
using Animora.Persistence.PostgreSql.Replication;
using Animora.Server.Commands;
using Animora.Server.Hosting;
using Microsoft.Extensions.Options;
namespace Animora.Server.Workers;

/// <summary>
/// Recovers expired intake leases. BUG-RECOVERY-01: the drain is bounded PER TENANT (a single tenant with a
/// deep backlog cannot starve the rest) and by a total per-tick ceiling, tenants are processed with bounded
/// parallelism, and each tenant unit takes the WORKER admission gate (never the request-path sync gate) only
/// for its own duration, so the gate - not a serial loop - is the real concurrency limit.
/// </summary>
internal sealed class IntakeRecoveryWorker : ResilientWorker
{
    private readonly IServiceScopeFactory _scopes;
    private readonly WorkerTenantDirectory _tenants;
    private readonly IBackgroundAdmission _admission;
    private readonly WorkerSettings _settings;
    public IntakeRecoveryWorker(IServiceScopeFactory scopes, WorkerTenantDirectory tenants, IBackgroundAdmission admission,
        IOptions<ServerOptions> options, TimeProvider clock, WorkerHeartbeat heartbeat, ILogger<IntakeRecoveryWorker> logger)
        : base("intake-recovery", TimeSpan.FromSeconds(30), clock, heartbeat, logger)
    { _scopes = scopes; _tenants = tenants; _admission = admission; _settings = options.Value.Workers; }

    protected override async Task TickAsync(CancellationToken ct)
    {
        int remaining = _settings.RecoveryTotalCap;
        var parallel = new ParallelOptions { MaxDegreeOfParallelism = _settings.RecoveryParallelism, CancellationToken = ct };
        await Parallel.ForEachAsync(_tenants.ReadAsync(ct), parallel, async (tenant, token) =>
        {
            if (Volatile.Read(ref remaining) <= 0) return;
            try { await RecoverTenantAsync(tenant, () => Interlocked.Decrement(ref remaining) >= 0, token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            // One failing tenant must not starve the others; the heartbeat/log gate reports the class once per minute.
            catch (Exception ex) { Logger.LogWarning("Intake recovery tenant failure class {FailureClass}", ex.GetType().Name); }
        }).ConfigureAwait(false);
    }

    private async Task RecoverTenantAsync(string tenant, Func<bool> takeBudget, CancellationToken ct)
    {
        await using var lease = await _admission.EnterAsync(ct).ConfigureAwait(false);
        await using var scope = _scopes.CreateAsyncScope(); var sp = scope.ServiceProvider;
        sp.GetRequiredService<WorkerExecutionContext>().IsWorker = true;
        sp.GetRequiredService<AuthTenantContext>().Bind(tenant);
        var store = sp.GetRequiredService<ICommandIntakeStore>();
        var rows = await store.ListExpiredLeasesAsync(Clock.GetUtcNow(), _settings.RecoveryPerTenantCap, ct).ConfigureAwait(false);
        foreach (var row in rows)
        {
            if (!takeBudget()) return;
            var result = await sp.GetRequiredService<IntakeRecoveryService>().RecoverAsync(row, ct).ConfigureAwait(false);
            if (result.IsSuccess)
                // S06 records intentionally omit auth identity/aggregate concurrency evidence.
                // Never invent authority to replay money effects. Release as fenced Failed so
                // an authenticated retry can safely replay with current policies and envelope.
                await store.MarkFailedAsync(row.OperationId, result.Value!, "awaiting_authenticated_replay", ct).ConfigureAwait(false);
        }
    }
}
