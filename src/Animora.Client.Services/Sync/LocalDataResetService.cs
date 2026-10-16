using Animora.Application.Security;
using Animora.Contracts.Diagnostics;
using Animora.Contracts.Sync;
using Animora.Domain.Results;
using Animora.Persistence.Sqlite.Maintenance;
using Animora.Sync.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Client.Services.Sync;

/// <summary>
/// Implements the Settings "wipe everything local and pull it all back from the server" action. Two ordered steps, serialised
/// against the background scheduler by the sync transition gate (held inside <see cref="ILocalStoreMaintenance.ResetForResyncAsync"/>):
/// <list type="number">
/// <item>Destructively clear the whole local replica of the current clinic (business data + sync cursor + replica base/checkpoint),
/// keeping only the installation identity, then rebind + re-seed the current tenant.</item>
/// <item>Pull a complete fresh snapshot from the server (<see cref="ISyncResyncHandler"/> → <c>POST /sync/snapshots</c>) so the device
/// ends up consistent with server state, and refresh the surfaced sync status.</item>
/// </list>
/// If the server is unreachable for step 2 the wipe still stands (it committed first) and the device is left in a clean, empty
/// state that the normal scheduler will fill on the next online pass — reported as <see cref="LocalResetOutcome.ClearedPendingServer"/>.
/// </summary>
public sealed class LocalDataResetService(
    IServiceScopeFactory scopes,
    ISyncResyncHandler resync,
    LocalSyncStatusService status,
    ISessionStore? sessions = null) : ILocalDataResetService
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <inheritdoc />
    public async Task<LocalResetResult> ResetAndResyncAsync(CancellationToken ct = default)
    {
        // One reset at a time per process; a second click must not race the wipe.
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Honour cancellation only BEFORE anything destructive happens.
            ct.ThrowIfCancellationRequested();

            Result wipe;
            await using (var scope = scopes.CreateAsyncScope())
            {
                var maintenance = scope.ServiceProvider.GetRequiredService<ILocalStoreMaintenance>();
                wipe = await maintenance.ResetForResyncAsync(ct).ConfigureAwait(false);
            }
            if (wipe.IsFailure)
            {
                // Nothing was cleared (the maintenance op is atomic) — the existing local data is intact.
                if (wipe.Error == ErrorCodes.StoreUnavailable && wipe.Detail == "reset_requires_bound_clinic")
                    return new LocalResetResult(LocalResetOutcome.NotBound, wipe.Detail);
                TraceLog.Error($"LocalDataReset: wipe FAILED ({wipe.Error}) detail='{wipe.Detail}'. Local data left intact.", null, "sync");
                return new LocalResetResult(LocalResetOutcome.Failed, wipe.Detail);
            }

            TraceLog.Info("LocalDataReset: local store wiped — pulling a full server snapshot …", "sync");

            // Step 2: re-pull everything. The resync handler is the SAME non-destructive full-snapshot path the engine uses when
            // the server demands a resync; here we invoke it deliberately after the wipe so the empty replica is rebuilt from the
            // current server state. A reset while signed out (no online session) cannot reach the protected /sync routes, so the
            // device is simply left clean for the scheduler to fill once a session exists.
            if (!await HasSessionAsync(ct).ConfigureAwait(false))
            {
                TraceLog.Info("LocalDataReset: no online session — leaving a clean store for the next online pass.", "sync");
                await RefreshStatusQuietlyAsync().ConfigureAwait(false);
                return new LocalResetResult(LocalResetOutcome.ClearedPendingServer, "no_session");
            }

            var pulled = await resync.HandleAsync(SyncScope.Default, "user_requested_reset", ct).ConfigureAwait(false);
            await RefreshStatusQuietlyAsync().ConfigureAwait(false);
            if (pulled.IsFailure)
            {
                // The wipe already committed; the server was just unreachable/failed for the re-pull. The next scheduled pass
                // (or a manual sync) completes the full pull against the now-empty replica.
                TraceLog.Warn($"LocalDataReset: snapshot pull did not complete ({pulled.Error}) detail='{pulled.Detail}'. Store is clean; next pass will finish.", "sync");
                return new LocalResetResult(LocalResetOutcome.ClearedPendingServer, pulled.Detail ?? pulled.Error.Code);
            }

            TraceLog.Info("LocalDataReset: full snapshot applied — device is in sync with the server.", "sync");
            return new LocalResetResult(LocalResetOutcome.ResyncedFromServer);
        }
        finally { _gate.Release(); }
    }

    private async Task<bool> HasSessionAsync(CancellationToken ct)
    {
        if (sessions is null) return false;
        var read = await sessions.ReadAsync(ct).ConfigureAwait(false);
        return read.IsSuccess && read.Value is not null;
    }

    // The status refresh is a best-effort cosmetic update of the topbar; its failure must not change the reset outcome.
    private async Task RefreshStatusQuietlyAsync()
    {
        try { await status.RefreshAsync(CancellationToken.None).ConfigureAwait(false); }
        catch (Exception) { /* status is informational only */ }
    }
}
