using Animora.Domain.Results;

namespace Animora.Sync.Abstractions;

/// <summary>Seam invoked when the server demands a full resync; the real shadow-generation handler replaces the no-op default.</summary>
public interface ISyncResyncHandler
{
    /// <summary>Rebuilds the local replica for <paramref name="scope"/>; a failure must leave live state untouched.</summary>
    Task<Result> HandleAsync(SyncScope scope, string reason, CancellationToken ct = default);
}

/// <summary>No-op default: acknowledges the request without touching any state (offline / unconfigured).</summary>
public sealed class NullSyncResyncHandler : ISyncResyncHandler
{
    /// <summary>Always succeeds without doing anything; honours a pre-cancelled token.</summary>
    public Task<Result> HandleAsync(SyncScope scope, string reason, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Result.Ok());
    }
}
