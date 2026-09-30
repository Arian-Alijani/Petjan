using Animora.Application.Commands;
using Animora.Application.Ports;
using Animora.Contracts.Diagnostics;
using Animora.Contracts.Sync;
using Animora.Domain.Time;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Client.Services.Sync;

/// <summary>One bounded real pass over the local journal; no simulated connectivity or EF dependencies.</summary>
public sealed class LocalSyncStatusService(IServiceScopeFactory scopes, ICommandTransport transport,
    IClock clock, IStoreReadinessGate readiness) : ISyncStatusService, IDisposable
{
    private readonly SemaphoreSlim _pass = new(1, 1);
    // NullCommandTransport explicitly means there is no configured server endpoint (until Track E).
    private readonly bool _offline = transport is NullCommandTransport;
    public SyncState State { get; private set; } = transport is NullCommandTransport ? SyncState.Offline : SyncState.Idle;
    public DateTimeOffset? LastSyncedAt { get; private set; }
    public event EventHandler? StateChanged;

    /// <summary>Read persisted status after store startup and after local business changes.</summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        await _pass.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await readiness.WaitUntilReadyAsync(ct).ConfigureAwait(false);
            await using var scope = scopes.CreateAsyncScope();
            await RefreshCoreAsync(scope.ServiceProvider, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { Publish(SyncState.Error, LastSyncedAt); }
        finally { _pass.Release(); }
    }

    public async Task SyncNowAsync(CancellationToken ct = default)
    {
        // Concurrent clicks do not run overlapping passes or dispose a captured scoped context.
        if (!await _pass.WaitAsync(0, ct).ConfigureAwait(false)) return;
        var previous = State;
        try
        {
            await readiness.WaitUntilReadyAsync(ct).ConfigureAwait(false);
            Publish(SyncState.Syncing, LastSyncedAt);
            await using var scope = scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var pass = await services.GetRequiredService<LocalCommandProcessor>().RunPassAsync(ct).ConfigureAwait(false);
            if (pass.IsFailure || pass.Value!.Failed > 0)
            {
                Publish(SyncState.Error, LastSyncedAt);
                return;
            }
            await services.GetRequiredService<ISyncCursorStore>().MarkSyncedAsync(clock.UtcNow, ct).ConfigureAwait(false);
            await RefreshCoreAsync(services, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Publish(previous, LastSyncedAt);
            throw;
        }
        catch (Exception) { Publish(SyncState.Error, LastSyncedAt); }
        finally { _pass.Release(); }
    }

    private async Task RefreshCoreAsync(IServiceProvider services, CancellationToken ct)
    {
        var summary = await services.GetRequiredService<ICommandJournal>().GetSummaryAsync(ct).ConfigureAwait(false);
        var marker = await services.GetRequiredService<ISyncCursorStore>().ReadAsync(ct).ConfigureAwait(false);
        Publish(summary.IsFailure ? SyncState.Error : _offline ? SyncState.Offline
            : summary.Value!.Total > 0 ? SyncState.Pending : SyncState.Synced, marker);
    }

    private void Publish(SyncState state, DateTimeOffset? marker)
    {
        if (State == state && LastSyncedAt == marker) return;
        State = state;
        LastSyncedAt = marker;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => _pass.Dispose();
}
