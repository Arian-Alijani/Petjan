using Animora.Application.Ports;
using Animora.Domain.Results;
using Animora.Sync.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Client.Services.Sync;

/// <summary>
/// Drives <see cref="IStateSyncEngine.PullAsync"/> per scope. The only place the C11 sync cursor advances for a
/// state pull — and only when the pull actually reflected server state (<c>Applied</c>/<c>UpToDate</c>).
/// Independent from the command-outbox pipeline (two-pipeline rule).
/// </summary>
public sealed class SyncOrchestrator(
    IStateSyncEngine engine,
    IServiceScopeFactory scopes,
    SyncScheduleOptions options,
    ISyncResyncHandler resync)
{
    /// <summary>Pulls one scope; expected engine outcomes are values, never exceptions.</summary>
    public async Task<Result<SyncOutcome>> PullOnceAsync(SyncScope scope, CancellationToken ct = default)
    {
        var pull = await engine.PullAsync(scope, ct).ConfigureAwait(false);
        if (pull.IsFailure) return Result<SyncOutcome>.Fail(pull.Error, pull.Detail);

        var result = pull.Value!;
        switch (result.Outcome)
        {
            case SyncOutcome.Applied:
            case SyncOutcome.UpToDate:
                await using (var scoped = scopes.CreateAsyncScope())
                {
                    await scoped.ServiceProvider.GetRequiredService<ISyncCursorStore>()
                        .MarkSyncedAsync(result.CompletedAtUtc, ct).ConfigureAwait(false);
                }
                return Result<SyncOutcome>.Ok(result.Outcome);

            case SyncOutcome.ResyncRequired:
                // The handler owns rebuilding; the cursor stays put until a later pull succeeds.
                await resync.HandleAsync(scope, result.Detail ?? "resync", ct).ConfigureAwait(false);
                return Result<SyncOutcome>.Ok(SyncOutcome.ResyncRequired);

            default:
                return Result<SyncOutcome>.Ok(result.Outcome);
        }
    }

    /// <summary>Pulls every configured scope and folds the results into one signal (fault/resync &gt; deferred &gt; applied &gt; up-to-date).</summary>
    public async Task<Result<SyncOutcome>> PullAllAsync(CancellationToken ct = default)
    {
        Result<SyncOutcome>? fault = null;
        var worst = SyncOutcome.UpToDate;
        var names = options.Scopes is { Count: > 0 } ? options.Scopes : ["default"];

        foreach (var name in names)
        {
            var scope = SyncScope.Create(name);
            var outcome = scope.IsFailure
                ? Result<SyncOutcome>.Fail(scope.Error, scope.Detail)
                : await PullOnceAsync(scope.Value, ct).ConfigureAwait(false);

            if (outcome.IsFailure) { fault ??= outcome; continue; }
            if (Rank(outcome.Value) > Rank(worst)) worst = outcome.Value;
        }

        if (fault is { } failed) return failed;
        return Result<SyncOutcome>.Ok(worst);
    }

    private static int Rank(SyncOutcome outcome) => outcome switch
    {
        SyncOutcome.UpToDate => 0,
        SyncOutcome.Applied => 1,
        SyncOutcome.Deferred => 2,
        SyncOutcome.ResyncRequired => 3,
        _ => 3,
    };
}
