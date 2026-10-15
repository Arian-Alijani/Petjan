using Animora.Contracts.Wire.Sync;
using Animora.Domain.Results;
using Animora.Sync.Abstractions;

namespace Animora.Sync.Custom;

/// <summary>
/// G04 full-resync handler: downloads a complete snapshot (<c>OpenSnapshotAsync()</c> → <c>POST /sync/snapshots</c>, the dedicated
/// retention-independent full-snapshot route) into the apply store's isolated shadow area while the live base, checkpoint and pending
/// command overlay stay readable and untouched, validates it (ordering, page count, per-page and batch content hash), and only then lets
/// the store publish it as a NEW base generation and switch the checkpoint in one transaction. Any failure aborts the session, so live
/// state is never cleared before its replacement succeeds.
/// <para>
/// It must NOT reuse the incremental <c>OpenBatchAsync(epoch, fromSequence: 0)</c> path: that hits the retention-enforcing
/// <c>/sync/batches</c> route, which answers <c>ResyncRequired(BEYOND_RETENTION)</c> whenever sequence 0 has already been pruned —
/// exactly the case that triggers a resync — leaving the handler unable to rebuild and spinning the pull loop forever.
/// </para>
/// </summary>
public sealed class ShadowResyncHandler(SyncFeedClient feed, ISyncApplyStore store, ServerEndpointOptions options) : ISyncResyncHandler
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <inheritdoc />
    public async Task<Result> HandleAsync(SyncScope scope, string reason, CancellationToken ct = default)
    {
        if (!options.IsConfigured) return Result.Ok();

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await RebuildAsync(scope, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Result> RebuildAsync(SyncScope scope, CancellationToken ct)
    {
        // Full snapshots are served by the dedicated /sync/snapshots route, which ignores retention and always freezes the
        // current epoch. (The server's current epoch is authoritative — a resync is usually triggered precisely because the
        // local epoch is stale — so the snapshot route resolves the epoch itself; there is no epoch/fromSequence to pass.)
        var opened = await feed.OpenSnapshotAsync(ct).ConfigureAwait(false);
        if (opened.IsFailure) return Result.Fail(opened.Error, opened.Detail);

        var outcome = opened.Value!;
        if (outcome.Kind != OpenBatchKind.Ready || outcome.Batch is null)
            return Result.Fail(ErrorCodes.SyncProtocolViolation, $"Full snapshot was not offered by the server ({outcome.Kind}).");

        var batch = outcome.Batch;
        if (batch.FromSequenceExclusive != 0)
            return Result.Fail(ErrorCodes.SyncShadowValidationFailed, $"Batch {batch.BatchId} is not a full snapshot (from {batch.FromSequenceExclusive}).");
        if (batch.PageCount < 1 || batch.PageCount > options.MaxBatchPages)
            return Result.Fail(ErrorCodes.SyncShadowValidationFailed, $"Snapshot advertises {batch.PageCount} pages (allowed 1..{options.MaxBatchPages}).");

        var begun = await store.BeginAsync(scope, batch, ct).ConfigureAwait(false);
        if (begun.IsFailure) return Result.Fail(begun.Error, begun.Detail);

        var session = begun.Value!;
        await using (session.ConfigureAwait(false))
        {
            var built = await BuildShadowAsync(session, batch, ct).ConfigureAwait(false);
            if (built.IsFailure)
            {
                await AbortQuietlyAsync(session).ConfigureAwait(false);
                return built;
            }

            var committed = await session.CommitAsync(ct).ConfigureAwait(false);
            if (committed.IsFailure)
            {
                await AbortQuietlyAsync(session).ConfigureAwait(false);
                return Result.Fail(committed.Error, committed.Detail);
            }

            return Result.Ok();
        }
    }

    private async Task<Result> BuildShadowAsync(ISyncApplySession session, BatchReady batch, CancellationToken ct)
    {
        var pageHashes = new List<string>(batch.PageCount);
        for (var ordinal = 0; ordinal < batch.PageCount; ordinal++)
        {
            var fetched = await feed.GetPageAsync(batch.BatchId, ordinal, null, ct).ConfigureAwait(false);
            if (fetched.IsFailure) return Result.Fail(fetched.Error, fetched.Detail);

            var page = fetched.Value;
            if (page is null || page.BatchId != batch.BatchId || page.PageOrdinal != ordinal || page.PageCount != batch.PageCount)
                return Result.Fail(ErrorCodes.SyncShadowValidationFailed, $"Snapshot page {ordinal} does not match batch {batch.BatchId}.");

            var accepted = await session.AcceptPageAsync(page, ct).ConfigureAwait(false);
            if (accepted.IsFailure) return Result.Fail(accepted.Error, accepted.Detail);

            pageHashes.Add(page.PageHash);
        }

        return string.Equals(PageIntegrity.ComputeContentHash(pageHashes), batch.ContentHash, StringComparison.OrdinalIgnoreCase)
            ? Result.Ok()
            : Result.Fail(ErrorCodes.SyncShadowValidationFailed, $"Snapshot {batch.BatchId} content hash does not match its pages.");
    }

    private static async Task AbortQuietlyAsync(ISyncApplySession session)
    {
        try
        {
            await session.AbortAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Best-effort cleanup; the validation failure is what the caller must see.
        }
    }
}
