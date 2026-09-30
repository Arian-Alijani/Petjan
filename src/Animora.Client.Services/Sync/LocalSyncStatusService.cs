using Animora.Application.Commands;
using Animora.Application.Ports;
using Animora.Contracts.Sync;
using Animora.Domain.Time;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Client.Services.Sync;

/// <summary>Real local status; E04 replaces the engine internals, not this registration.
/// Singleton, with fresh scoped persistence per call; NullCommandTransport means no endpoint, not an error.</summary>
public sealed class LocalSyncStatusService(IServiceScopeFactory scopes, ICommandTransport transport, IClock clock)
    : ISyncStatusService, IDisposable
{
    private readonly SemaphoreSlim _pass = new(1, 1);
    private sealed record Snapshot(SyncState State, DateTimeOffset? LastSyncedAt);
    private Snapshot _snapshot = new(transport is NullCommandTransport ? SyncState.Offline : SyncState.Idle, null);
    public SyncState State => Volatile.Read(ref _snapshot).State;
    public DateTimeOffset? LastSyncedAt => Volatile.Read(ref _snapshot).LastSyncedAt;
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
            if (runPass)
            {
                Publish(new(SyncState.Syncing, previous.LastSyncedAt));
                var result = await scope.ServiceProvider.GetRequiredService<LocalCommandProcessor>()
                    .RunPassAsync(ct).ConfigureAwait(false);
                if (result.IsFailure || result.Value!.Failed > 0)
                {
                    Publish(new(SyncState.Error, previous.LastSyncedAt));
                    return;
                }
                await cursor.MarkSyncedAsync(clock.UtcNow, ct).ConfigureAwait(false);
            }
            var last = await cursor.ReadAsync(ct).ConfigureAwait(false);
            var summary = await scope.ServiceProvider.GetRequiredService<ICommandJournal>()
                .GetSummaryAsync(ct).ConfigureAwait(false);
            var state = summary.IsFailure ? SyncState.Error
                : transport is NullCommandTransport ? SyncState.Offline
                : summary.Value!.Total > 0 ? SyncState.Pending : SyncState.Synced;
            Publish(new(state, last));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Publish(previous);
        }
        catch (Exception)
        {
            // Store/transport failure is surfaced through the contract, never a simulated success.
            Publish(new(SyncState.Error, previous.LastSyncedAt));
        }
        finally { _pass.Release(); }
    }

    private void Publish(Snapshot next)
    {
        var previous = Interlocked.Exchange(ref _snapshot, next);
        if (previous != next) StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => _pass.Dispose();
}
