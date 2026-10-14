using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Contracts.Diagnostics;
using Animora.Domain.Results;
using Animora.Sync.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Client.Services.Sync;

/// <summary>
/// Drives <see cref="IStateSyncEngine.PullAsync"/> per scope. The only place the C11 sync cursor advances for a
/// state pull — and only when the pull actually reflected server state (<c>Applied</c>/<c>UpToDate</c>).
/// Independent from the command-outbox pipeline (two-pipeline rule).
/// <para>
/// <b>Session gate.</b> When an <see cref="ISessionStore"/> is supplied, no request (manifest or batch) leaves the
/// device until an authenticated online session exists. A device that is only <c>Pending</c> approval never keeps a
/// session (<c>OnlineAuthClient</c> drops it), so the pull stays <see cref="SyncOutcome.Deferred"/> instead of hitting
/// the protected <c>/sync/*</c> routes anonymously.
/// </para>
/// </summary>
public sealed class SyncOrchestrator(
    IStateSyncEngine engine,
    IServiceScopeFactory scopes,
    SyncScheduleOptions options,
    ISyncResyncHandler resync,
    ISessionStore? sessions = null)
{
    /// <summary>Pulls one scope; expected engine outcomes are values, never exceptions.</summary>
    public async Task<Result<SyncOutcome>> PullOnceAsync(SyncScope scope, CancellationToken ct = default)
    {
        if (!await HasSessionAsync(ct).ConfigureAwait(false))
        {
            TraceLog.Info($"SyncOrchestrator.PullOnce scope='{scope.Name}' → no authenticated online session → Deferred (no request leaves the device).", "sync");
            return Result<SyncOutcome>.Ok(SyncOutcome.Deferred);
        }

        TraceLog.Info($"SyncOrchestrator.PullOnce scope='{scope.Name}' → engine.PullAsync …", "sync");
        var pull = await engine.PullAsync(scope, ct).ConfigureAwait(false);
        if (pull.IsFailure)
        {
            TraceLog.Error($"SyncOrchestrator.PullOnce scope='{scope.Name}' → engine FAILED ({pull.Error}) detail='{pull.Detail}'.", null, "sync");
            return Result<SyncOutcome>.Fail(pull.Error, pull.Detail);
        }

        var result = pull.Value!;
        TraceLog.Info($"SyncOrchestrator.PullOnce scope='{scope.Name}' ← outcome={result.Outcome}, watermark={result.Watermark?.ToString() ?? "(none)"}, batches={result.BatchesApplied}, records={result.RecordsApplied}, detail='{result.Detail}'.", "sync");
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
                TraceLog.Warn($"SyncOrchestrator.PullOnce scope='{scope.Name}' → RESYNC REQUIRED ({result.Detail ?? "resync"}) — rebuilding replica.", "sync");
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

    private async Task<bool> HasSessionAsync(CancellationToken ct)
    {
        if (sessions is null) return true; // no session port wired (offline/unit composition): the engine's own configuration gate applies
        var read = await sessions.ReadAsync(ct).ConfigureAwait(false);
        return read.IsSuccess && read.Value is { AccessToken.Length: > 0 };
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
