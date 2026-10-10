using Animora.Contracts.Wire.Sync;
using Animora.Domain.Results;
using Animora.Sync.Abstractions;

namespace Animora.Sync.Custom;

/// <summary>The replication position the local base reflects: the server epoch and ChangeSequence watermark.</summary>
public readonly record struct SyncCheckpoint(long Epoch, long Watermark);

/// <summary>Seam between download+verify (this project) and the real atomic apply to the local store (G04).</summary>
public interface ISyncApplyStore
{
    /// <summary>Reads the last committed checkpoint for <paramref name="scope"/> ((0,0) when nothing was ever applied).</summary>
    Task<Result<SyncCheckpoint>> ReadCheckpointAsync(SyncScope scope, CancellationToken ct = default);

    /// <summary>Starts an apply session for one frozen batch; nothing is visible until <see cref="ISyncApplySession.CommitAsync"/>.</summary>
    Task<Result<ISyncApplySession>> BeginAsync(SyncScope scope, BatchReady batch, CancellationToken ct = default);
}

/// <summary>One all-or-nothing apply of a batch: pages are accepted in order, then committed or aborted.</summary>
public interface ISyncApplySession : IAsyncDisposable
{
    /// <summary>Accepts the next verified page (strictly in ordinal order).</summary>
    Task<Result> AcceptPageAsync(SyncPage page, CancellationToken ct = default);

    /// <summary>Atomically publishes the batch and advances the checkpoint to the batch epoch/watermark.</summary>
    Task<Result<SyncCheckpoint>> CommitAsync(CancellationToken ct = default);

    /// <summary>Discards everything accepted so far; the checkpoint is unchanged.</summary>
    Task<Result> AbortAsync(CancellationToken ct = default);
}
