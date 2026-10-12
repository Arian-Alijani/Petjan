using Animora.Application.Commands;
using Animora.Application.Ports;
using Animora.Application.UseCases.Sync;
using Animora.Domain.Results;
using Animora.Contracts.Sync;
using Animora.Domain.Time;
using Animora.Sync.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Client.Services.Sync;

/// <summary>Real local status; E04 replaces the engine internals, not this registration.
/// Singleton, with fresh scoped persistence per call; NullCommandTransport means no endpoint, not an error.
/// G03: when a configured state-pull engine is supplied, its outcome is combined with the command backlog into ONE state
/// (two pipelines, never merged).</summary>
public sealed class LocalSyncStatusService(
    IServiceScopeFactory scopes,
    ICommandTransport transport,
    IClock clock,
    IStateSyncEngine? engine = null,
    SyncOrchestrator? orchestrator = null)
    : ISyncStatusService, IDisposable
{
    private readonly SemaphoreSlim _pass = new(1, 1);
    private sealed record Snapshot(SyncState State, DateTimeOffset? LastSyncedAt, int PendingChangeCount = 0);
    private bool PullConfigured => orchestrator is not null && engine is { IsConfigured: true };
    private Snapshot _snapshot = new(transport is NullCommandTransport && engine is not { IsConfigured: true } ? SyncState.Offline : SyncState.Idle, null);
    public SyncState State => Volatile.Read(ref _snapshot).State;
    public DateTimeOffset? LastSyncedAt => Volatile.Read(ref _snapshot).LastSyncedAt;
    public int PendingChangeCount => Volatile.Read(ref _snapshot).PendingChangeCount;
    public event EventHandler? StateChanged;

    /// <summary>Load durable status after sign-in, without running a processor pass or blocking first frame.</summary>
    public Task RefreshAsync(CancellationToken ct = default) => ExecuteAsync(runPass: false, ct);
    public Task SyncNowAsync(CancellationToken ct = default) => ExecuteAsync(runPass: true, ct);

    private async Task ExecuteAsync(bool runPass, CancellationToken ct)
    {
        // Cancellation while waiting must not change another caller's state.
        await _pass.WaitAsync(ct).ConfigureAwait(false);
        var previous = Volatile.Read(ref _snapshot);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var cursor = scope.ServiceProvider.GetRequiredService<ISyncCursorStore>();
            Result<SyncOutcome>? pull = null;
            if (runPass)
            {
                Publish(new(SyncState.Syncing, previous.LastSyncedAt, previous.PendingChangeCount));
                var result = await scope.ServiceProvider.GetRequiredService<LocalCommandProcessor>()
                    .RunPassAsync(ct).ConfigureAwait(false);
                if (result.IsFailure || result.Value!.Failed > 0)
                {
                    Publish(new(SyncState.Error, previous.LastSyncedAt, previous.PendingChangeCount));
                    return;
                }
                if (PullConfigured)
                    pull = await orchestrator!.PullAllAsync(ct).ConfigureAwait(false);
                // The command pass only counts as "synced" when no state-pull is configured or that pull was healthy.
                if (pull is null || pull.Value.IsSuccess)
                    await cursor.MarkSyncedAsync(clock.UtcNow, ct).ConfigureAwait(false);
            }
            var last = await cursor.ReadAsync(ct).ConfigureAwait(false);
            var summary = await scope.ServiceProvider.GetRequiredService<ICommandJournal>()
                .GetSummaryAsync(ct).ConfigureAwait(false);
            // Surface the unsent-backlog size so the topbar can explain whether a manual sync has outbound work
            // to do; fall back to the previous count if the summary read failed.
            var pendingCount = summary.IsSuccess ? summary.Value!.Total : previous.PendingChangeCount;
            Publish(new(MapState(summary, pull), last, pendingCount));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Publish(previous);
        }
        catch (Exception)
        {
            // Store/transport failure is surfaced through the contract, never a simulated success.
            Publish(new(SyncState.Error, previous.LastSyncedAt, previous.PendingChangeCount));
        }
        finally { _pass.Release(); }
    }

    private SyncState MapState(Result<PendingCommandSummary> summary, Result<SyncOutcome>? pull)
    {
        if (summary.IsFailure) return SyncState.Error;
        if (pull is { IsFailure: true } || pull is { Value: SyncOutcome.ResyncRequired }) return SyncState.Error;
        if (transport is NullCommandTransport && !PullConfigured) return SyncState.Offline;
        if (summary.Value!.Total > 0) return SyncState.Pending;
        // A pass that deferred its pull (server unreachable) is not "synced".
        return pull is { Value: SyncOutcome.Deferred } ? SyncState.Offline : SyncState.Synced;
    }

    private void Publish(Snapshot next)
    {
        var previous = Interlocked.Exchange(ref _snapshot, next);
        if (previous != next) StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => _pass.Dispose();
}
