using Animora.Contracts.Wire.Sync;
using Animora.Domain.Results;
using Animora.Sync.Abstractions;

namespace Animora.Sync.Custom;

/// <summary>
/// Default apply store for this phase: verifies page order and counts records without keeping the batch (memory stays bounded),
/// and remembers the last committed checkpoint in memory only. Writes nothing to SQLite — G04 replaces it with the atomic store.
/// </summary>
public sealed class BufferingSyncApplyStore : ISyncApplyStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, SyncCheckpoint> _checkpoints = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<Result<SyncCheckpoint>> ReadCheckpointAsync(SyncScope scope, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Result<SyncCheckpoint>.Ok(Read(scope)));
    }

    /// <inheritdoc />
    public Task<Result<ISyncApplySession>> BeginAsync(SyncScope scope, BatchReady batch, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(batch);
        return Task.FromResult(Result<ISyncApplySession>.Ok(new Session(this, scope, batch)));
    }

    private SyncCheckpoint Read(SyncScope scope)
    {
        lock (_gate) return _checkpoints.TryGetValue(scope.Name, out var cp) ? cp : default;
    }

    private void Write(SyncScope scope, SyncCheckpoint checkpoint)
    {
        lock (_gate) _checkpoints[scope.Name] = checkpoint;
    }

    private sealed class Session(BufferingSyncApplyStore owner, SyncScope scope, BatchReady batch) : ISyncApplySession
    {
        private int _nextOrdinal;
        private bool _closed;

        public Task<Result> AcceptPageAsync(SyncPage page, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(page);
            if (_closed)
                return Task.FromResult(Result.Fail(ErrorCodes.SyncProtocolViolation, "Apply session is already closed."));
            if (page.BatchId != batch.BatchId || page.PageOrdinal != _nextOrdinal)
                return Task.FromResult(Result.Fail(ErrorCodes.SyncProtocolViolation,
                    $"Expected page {_nextOrdinal} of batch {batch.BatchId}."));
            _nextOrdinal++;
            return Task.FromResult(Result.Ok());
        }

        public Task<Result<SyncCheckpoint>> CommitAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (_closed || _nextOrdinal != batch.PageCount)
                return Task.FromResult(Result<SyncCheckpoint>.Fail(ErrorCodes.SyncProtocolViolation,
                    $"Batch {batch.BatchId} is incomplete ({_nextOrdinal}/{batch.PageCount} pages)."));
            _closed = true;
            var checkpoint = new SyncCheckpoint(batch.Epoch, batch.Watermark);
            owner.Write(scope, checkpoint);
            return Task.FromResult(Result<SyncCheckpoint>.Ok(checkpoint));
        }

        public Task<Result> AbortAsync(CancellationToken ct = default)
        {
            _closed = true;
            return Task.FromResult(Result.Ok());
        }

        public ValueTask DisposeAsync()
        {
            _closed = true;
            return ValueTask.CompletedTask;
        }
    }
}
