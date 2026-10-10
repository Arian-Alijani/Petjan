using Animora.Domain.Results;
using Animora.Domain.Time;

namespace Animora.Sync.Abstractions;

/// <summary>Offline fallback engine: always defers (a success), mirrors NullCommandTransport. Default until a real adapter is registered.</summary>
public sealed class NullSyncEngine(IClock clock) : IStateSyncEngine
{
    /// <summary>Always false — no endpoint is configured.</summary>
    public bool IsConfigured => false;

    /// <summary>Returns a deferred (offline) success; honours a pre-cancelled token; accepts any scope.</summary>
    public Task<Result<SyncResult>> PullAsync(SyncScope scope, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Result<SyncResult>.Ok(
            SyncResult.Deferred(clock.UtcNow, "No sync engine configured (offline).")));
    }
}
