using Animora.Domain.Results;

namespace Animora.Sync.Abstractions;

/// <summary>The client-side state-replication seam: pull immutable batches and apply them atomically.</summary>
public interface IStateSyncEngine
{
    /// <summary>False for NullSyncEngine (no endpoint configured); true for a real adapter — lets status UI say "offline" not "error".</summary>
    bool IsConfigured { get; }

    /// <summary>Pull-only (G01–G04): download immutable batches for <paramref name="scope"/> and apply them atomically; never touches the pending overlay.</summary>
    Task<Result<SyncResult>> PullAsync(SyncScope scope, CancellationToken ct = default);
}
