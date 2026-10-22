using Animora.Application.Ports;
using Animora.Persistence.Sqlite.Context;
using Animora.Application.Security;
using Animora.Client.Services.Notifications;
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
        using var di = scopes.CreateScope();
        var state = di.ServiceProvider.GetService<LocalInstallationState>();
        if (state is not null) await state.TransitionGate.WaitAsync(ct).ConfigureAwait(false);
        try { return await PullBoundAsync(scope, ct).ConfigureAwait(false); }
        finally { state?.TransitionGate.Release(); }
    }

    private async Task<Result<SyncOutcome>> PullBoundAsync(SyncScope scope, CancellationToken ct)
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

        // D04b: after a healthy state pull, refresh the read-only peer finance cache. Only when a consumer is registered (a configured
        // server) — otherwise this is a no-op and offline behaviour is untouched. It is a display feed, so its failure never turns a
        // healthy state pull into a failed pass; it is logged and simply retried from the same cursor on the next pass.
        if (worst is SyncOutcome.Applied or SyncOutcome.UpToDate)
        {
            await PullPeerFinanceAsync(ct).ConfigureAwait(false);
            // Vendor messages composed in the admin panel (GET /notifications) + their real delivery receipts: same best-effort
            // contract as the finance cache — a failure here is logged under sync.notify and never fails the pass.
            await PullPeerNotificationsAsync(ct).ConfigureAwait(false);
        }
        return Result<SyncOutcome>.Ok(worst);
    }

    private async Task PullPeerFinanceAsync(CancellationToken ct)
    {
        try
        {
            if (!await HasSessionAsync(ct).ConfigureAwait(false)) return;
            using var di = scopes.CreateScope();
            if (di.ServiceProvider.GetService<IPeerFinanceSync>() is not { } finance) return;
            var state = di.ServiceProvider.GetService<LocalInstallationState>();
            if (state is not null) await state.TransitionGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var pulled = await finance.PullAsync(ct).ConfigureAwait(false);
                if (pulled.IsFailure)
                    TraceLog.Warn($"SyncOrchestrator.PullPeerFinance → {pulled.Error} ('{pulled.Detail}'); the cache keeps its last cursor.", "sync");
            }
            finally { state?.TransitionGate.Release(); }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            TraceLog.Error("SyncOrchestrator.PullPeerFinance faulted; the state pull result is unaffected.", ex, "sync");
        }
    }

    private async Task PullPeerNotificationsAsync(CancellationToken ct)
    {
        try
        {
            if (!await HasSessionAsync(ct).ConfigureAwait(false)) return;
            using var di = scopes.CreateScope();
            if (di.ServiceProvider.GetService<IPeerNotificationSync>() is not { } notifications) return;
            var state = di.ServiceProvider.GetService<LocalInstallationState>();
            if (state is not null) await state.TransitionGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var pulled = await notifications.PullAsync(ct).ConfigureAwait(false);
                if (pulled.IsFailure)
                    TraceLog.Warn($"SyncOrchestrator.PullPeerNotifications → {pulled.Error} ('{pulled.Detail}'); the feed keeps its last cursor.", "sync.notify");
                else if (pulled.Value > 0)
                {
                    TraceLog.Info($"SyncOrchestrator.PullPeerNotifications ← {pulled.Value} new message(s); refreshing the feed.", "sync.notify");
                    // The pull wrote rows straight into the local store; tell the (optional) notification service to re-read.
                    di.ServiceProvider.GetService<NotificationService>()?.NotifyFeedChanged();
                }
            }
            finally { state?.TransitionGate.Release(); }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            TraceLog.Error("SyncOrchestrator.PullPeerNotifications faulted; the state pull result is unaffected.", ex, "sync.notify");
        }
    }

    private async Task<bool> HasSessionAsync(CancellationToken ct)
    {
        if (sessions is null) return true; // no session port wired (offline/unit composition): the engine's own configuration gate applies
        var read = await sessions.ReadAsync(ct).ConfigureAwait(false);
        using var di = scopes.CreateScope();
        var state = di.ServiceProvider.GetService<LocalInstallationState>();
        return read.IsSuccess && read.Value is { AccessToken.Length: > 0 } session
            && (state is null || state.TenantId == session.TenantId);
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
