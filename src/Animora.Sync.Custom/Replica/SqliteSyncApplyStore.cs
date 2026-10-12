using Animora.Contracts.Wire.Sync;
using Animora.Domain.Results;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Replica;
using Animora.Sync.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Sync.Custom.Replica;

/// <summary>
/// G04 real apply store over the encrypted local SQLite file: pages stream into <c>repl_staging</c> (one short transaction per
/// page, bounded memory), and <see cref="ISyncApplySession.CommitAsync"/> publishes the batch onto <c>repl_base</c> and advances
/// <c>repl_checkpoint</c> in ONE transaction — a crash before that COMMIT leaves base and checkpoint exactly as they were.
/// <para>
/// A full snapshot batch (<c>FromSequenceExclusive == 0</c>) is built as a brand-new base generation and the checkpoint switches to it
/// atomically, so the active base is never cleared before its replacement is complete (non-destructive full resync). Only <c>repl_*</c>
/// tables are written by the replica store itself; the typed business projection is delegated to <see cref="ReplicaMaterializer"/> inside the same
/// commit transaction, and the pending command overlay is never touched.
/// </para>
/// Sessions are serialised per store; staging left by a crashed session is cleared when the next session begins.
/// </summary>
public sealed class SqliteSyncApplyStore(IServiceScopeFactory scopes) : ISyncApplyStore
{
    private const int MaxKeyLength = 128;
    private const int MaxPayloadLength = 262144;

    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <inheritdoc />
    public async Task<Result<SyncCheckpoint>> ReadCheckpointAsync(SyncScope scope, CancellationToken ct = default)
    {
        using var di = scopes.CreateScope();
        var db = di.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var tenant = di.ServiceProvider.GetRequiredService<ILocalTenantContext>().TenantId;
        var cp = await ReadAsync(db, tenant, scope.Name, ct).ConfigureAwait(false);
        return Result<SyncCheckpoint>.Ok(new SyncCheckpoint(cp.Epoch, cp.Watermark));
    }

    /// <inheritdoc />
    public async Task<Result<ISyncApplySession>> BeginAsync(SyncScope scope, BatchReady batch, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        IServiceScope? di = null;
        try
        {
            di = scopes.CreateScope();
            var db = di.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var tenant = di.ServiceProvider.GetRequiredService<ILocalTenantContext>().TenantId;
            // Scratch rows of a crashed/aborted earlier session are meaningless: staging never feeds anything but its own commit.
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM repl_staging WHERE TenantId = {tenant}", ct).ConfigureAwait(false);
            return Result<ISyncApplySession>.Ok(new Session(this, di, db, tenant, scope, batch));
        }
        catch
        {
            di?.Dispose();
            _gate.Release();
            throw;
        }
    }

    private static async Task<(long Epoch, long Watermark, long Generation)> ReadAsync(
        AnimoraLocalDbContext db, string tenant, string scope, CancellationToken ct)
    {
        var row = await db.Set<ReplicaCheckpoint>().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.Scope == scope)
            .Select(x => new { x.Epoch, x.Watermark, x.Generation })
            .SingleOrDefaultAsync(ct).ConfigureAwait(false);
        return row is null ? (0, 0, 0) : (row.Epoch, row.Watermark, row.Generation);
    }

    private sealed class Session(
        SqliteSyncApplyStore owner, IServiceScope di, AnimoraLocalDbContext db, string tenant, SyncScope scope, BatchReady batch)
        : ISyncApplySession
    {
        private int _nextOrdinal;
        private bool _closed;
        private bool _released;

        public async Task<Result> AcceptPageAsync(SyncPage page, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(page);
            if (_closed) return Result.Fail(ErrorCodes.SyncProtocolViolation, "Apply session is already closed.");
            if (page.BatchId != batch.BatchId || page.PageOrdinal != _nextOrdinal)
                return Result.Fail(ErrorCodes.SyncProtocolViolation, $"Expected page {_nextOrdinal} of batch {batch.BatchId}.");

            foreach (var record in page.Records)
            {
                if (record.Entity is not { Length: > 0 and <= MaxKeyLength } || record.EntityId is not { Length: > 0 and <= MaxKeyLength })
                    return Result.Fail(ErrorCodes.SyncProtocolViolation, $"Page {page.PageOrdinal} carries a record with an invalid entity key.");
            }

            await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            foreach (var record in page.Records)
            {
                var deleted = record.Op == 'D' || record.Deleted;
                var payload = deleted ? null : record.Payload?.GetRawText();
                if (payload is { Length: > MaxPayloadLength })
                    return Result.Fail(ErrorCodes.SyncProtocolViolation, $"Record {record.Entity}/{record.EntityId} payload exceeds the replica bound.");
                long? updated = record.UpdatedAt?.UtcTicks;
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO repl_staging (TenantId, BatchId, Entity, EntityId, PageOrdinal, Version, Payload, Deleted, UpdatedAtUtc)
                    VALUES ({tenant}, {batch.BatchId}, {record.Entity}, {record.EntityId}, {page.PageOrdinal}, {record.Version}, {payload}, {(deleted ? 1 : 0)}, {updated})
                    ON CONFLICT (TenantId, BatchId, Entity, EntityId) DO UPDATE SET
                        PageOrdinal = excluded.PageOrdinal, Version = excluded.Version, Payload = excluded.Payload,
                        Deleted = excluded.Deleted, UpdatedAtUtc = excluded.UpdatedAtUtc
                    """, ct).ConfigureAwait(false);
            }

            await tx.CommitAsync(ct).ConfigureAwait(false);
            _nextOrdinal++;
            return Result.Ok();
        }

        public async Task<Result<SyncCheckpoint>> CommitAsync(CancellationToken ct = default)
        {
            if (_closed || _nextOrdinal != batch.PageCount)
                return Result<SyncCheckpoint>.Fail(ErrorCodes.SyncProtocolViolation,
                    $"Batch {batch.BatchId} is incomplete ({_nextOrdinal}/{batch.PageCount} pages).");

            var snapshot = batch.FromSequenceExclusive == 0;
            await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            var current = await ReadAsync(db, tenant, scope.Name, ct).ConfigureAwait(false);

            if (!snapshot && (current.Epoch != batch.Epoch || current.Watermark < batch.FromSequenceExclusive
                              || batch.Watermark < current.Watermark))
                return Result<SyncCheckpoint>.Fail(ErrorCodes.SyncApplyConflict,
                    $"Batch {batch.BatchId} (from {batch.FromSequenceExclusive}) does not continue the local checkpoint ({current.Epoch}/{current.Watermark}).");

            var target = snapshot ? current.Generation + 1 : Math.Max(current.Generation, 1);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO repl_base (TenantId, Generation, Entity, EntityId, Version, Payload, Deleted, UpdatedAtUtc)
                SELECT TenantId, {target}, Entity, EntityId, Version, Payload, Deleted, UpdatedAtUtc
                FROM repl_staging WHERE TenantId = {tenant} AND BatchId = {batch.BatchId}
                ON CONFLICT (TenantId, Generation, Entity, EntityId) DO UPDATE SET
                    Version = excluded.Version, Payload = excluded.Payload,
                    Deleted = excluded.Deleted, UpdatedAtUtc = excluded.UpdatedAtUtc
                """, ct).ConfigureAwait(false);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO repl_checkpoint (TenantId, Scope, Epoch, Watermark, Generation, ServerGeneration)
                VALUES ({tenant}, {scope.Name}, {batch.Epoch}, {batch.Watermark}, {target}, {batch.Generation})
                ON CONFLICT (TenantId, Scope) DO UPDATE SET
                    Epoch = excluded.Epoch, Watermark = excluded.Watermark,
                    Generation = excluded.Generation, ServerGeneration = excluded.ServerGeneration
                """, ct).ConfigureAwait(false);
            // Project onto the typed business tables the screens read, in this same transaction (staging is still present here).
            await ReplicaMaterializer.ApplyAsync(db, tenant, batch.BatchId, ct).ConfigureAwait(false);
            if (snapshot)
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM repl_base WHERE TenantId = {tenant} AND Generation <> {target}", ct).ConfigureAwait(false);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM repl_staging WHERE TenantId = {tenant} AND BatchId = {batch.BatchId}", ct).ConfigureAwait(false);

            await tx.CommitAsync(ct).ConfigureAwait(false);
            _closed = true;
            return Result<SyncCheckpoint>.Ok(new SyncCheckpoint(batch.Epoch, batch.Watermark));
        }

        public async Task<Result> AbortAsync(CancellationToken ct = default)
        {
            _closed = true;
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM repl_staging WHERE TenantId = {tenant} AND BatchId = {batch.BatchId}", ct).ConfigureAwait(false);
            return Result.Ok();
        }

        public async ValueTask DisposeAsync()
        {
            if (_released) return;
            _released = true;
            try
            {
                if (!_closed) await AbortAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Best-effort cleanup; the next session clears any leftover staging at Begin.
            }
            finally
            {
                _closed = true;
                di.Dispose();
                owner._gate.Release();
            }
        }
    }
}
