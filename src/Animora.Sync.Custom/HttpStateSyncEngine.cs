using Animora.Contracts.Diagnostics;
using Animora.Contracts.Wire.Sync;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Sync.Abstractions;

namespace Animora.Sync.Custom;

/// <summary>
/// Pull-only <see cref="IStateSyncEngine"/> over the immutable-batch feed: manifest → open batch → download + verify every page →
/// verify the batch content hash → commit atomically through <see cref="ISyncApplyStore"/>. Any failure aborts the session, so the
/// checkpoint only ever moves on a fully verified batch; it never touches the pending command overlay and has no push path.
/// </summary>
public sealed class HttpStateSyncEngine : IStateSyncEngine
{
    private const int MaxStagedPages = 64;
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);

    private readonly SyncFeedClient _feed;
    private readonly ISyncApplyStore _store;
    private readonly ServerEndpointOptions _options;
    private readonly IClock _clock;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);

    // Pages verified by a pull that failed part-way; a retry of the same (immutable) batch revalidates them with If-None-Match.
    private string? _stagedBatchId;
    private readonly Dictionary<int, SyncPage> _staged = [];

    /// <summary>Creates the engine over its feed client, apply store, options and clock (optional <paramref name="timeProvider"/> for retry waits).</summary>
    public HttpStateSyncEngine(SyncFeedClient feed, ISyncApplyStore store, ServerEndpointOptions options, IClock clock, TimeProvider? timeProvider = null)
    {
        _feed = feed ?? throw new ArgumentNullException(nameof(feed));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public bool IsConfigured => _options.IsConfigured;

    /// <inheritdoc />
    public async Task<Result<SyncResult>> PullAsync(SyncScope scope, CancellationToken ct = default)
    {
        if (!IsConfigured)
            return Result<SyncResult>.Ok(SyncResult.Deferred(_clock.UtcNow, "No server endpoint configured (offline)."));

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await PullCoreAsync(scope, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Result<SyncResult>> PullCoreAsync(SyncScope scope, CancellationToken ct)
    {
        var checkpoint = await _store.ReadCheckpointAsync(scope, ct).ConfigureAwait(false);
        if (checkpoint.IsFailure)
        {
            TraceLog.Error($"SyncEngine.Pull scope='{scope.Name}' → ReadCheckpoint FAILED ({checkpoint.Error}) detail='{checkpoint.Detail}'.", null, "sync");
            return Result<SyncResult>.Fail(checkpoint.Error, checkpoint.Detail);
        }
        TraceLog.Info($"SyncEngine.Pull scope='{scope.Name}' — local checkpoint epoch={checkpoint.Value.Epoch}, watermark={checkpoint.Value.Watermark}.", "sync");

        TraceLog.Info("SyncEngine.Pull → GET /sync/manifest …", "sync");
        var manifest = await _feed.GetManifestAsync(ct).ConfigureAwait(false);
        if (manifest.IsFailure)
        {
            TraceLog.Error($"SyncEngine.Pull → manifest FAILED ({manifest.Error}) detail='{manifest.Detail}'.", null, "sync");
            return Result<SyncResult>.Fail(manifest.Error, manifest.Detail);
        }
        TraceLog.Info($"SyncEngine.Pull ← manifest: serverEpoch={manifest.Value!.Epoch}.", "sync");

        var (epoch0, from0) = (checkpoint.Value.Epoch, checkpoint.Value.Watermark);
        var serverEpoch = manifest.Value!.Epoch;
        if (epoch0 != 0 && epoch0 != serverEpoch)
        {
            TraceLog.Warn($"SyncEngine.Pull → EPOCH CHANGED (local={epoch0}, server={serverEpoch}) → ResyncRequired.", "sync");
            return Result<SyncResult>.Ok(SyncResult.ResyncRequired("EPOCH_CHANGED", _clock.UtcNow));
        }
        if (epoch0 == 0) from0 = 0;

        TraceLog.Info($"SyncEngine.Pull → POST /sync/batches (epoch={serverEpoch}, fromSequence={from0}) …", "sync");
        var opened = await _feed.OpenBatchAsync(serverEpoch, from0, ct).ConfigureAwait(false);
        if (opened.IsFailure)
        {
            TraceLog.Error($"SyncEngine.Pull → OpenBatch FAILED ({opened.Error}) detail='{opened.Detail}'.", null, "sync");
            return Result<SyncResult>.Fail(opened.Error, opened.Detail);
        }

        var outcome = opened.Value!;
        TraceLog.Info($"SyncEngine.Pull ← open-batch kind={outcome.Kind}, watermark={outcome.Watermark}, batchId={outcome.Batch?.BatchId ?? "(none)"}, pageCount={outcome.Batch?.PageCount.ToString() ?? "-"}.", "sync");
        switch (outcome.Kind)
        {
            case OpenBatchKind.Current:
                return Result<SyncResult>.Ok(SyncResult.UpToDate(outcome.Watermark, _clock.UtcNow));
            case OpenBatchKind.Resync:
                TraceLog.Warn($"SyncEngine.Pull → server requires RESYNC ({outcome.Reason ?? "UNKNOWN"}).", "sync");
                return Result<SyncResult>.Ok(SyncResult.ResyncRequired(outcome.Reason ?? "UNKNOWN", _clock.UtcNow));
            default:
                return await DownloadAndApplyAsync(scope, outcome.Batch!, ct).ConfigureAwait(false);
        }
    }

    private async Task<Result<SyncResult>> DownloadAndApplyAsync(SyncScope scope, BatchReady batch, CancellationToken ct)
    {
        if (batch.PageCount < 1 || batch.PageCount > _options.MaxBatchPages)
            return Result<SyncResult>.Fail(ErrorCodes.SyncProtocolViolation, $"Batch advertises {batch.PageCount} pages (allowed 1..{_options.MaxBatchPages}).");

        TraceLog.Info($"SyncEngine.DownloadAndApply — batch={batch.BatchId}, pages={batch.PageCount} …", "sync");
        if (_stagedBatchId != batch.BatchId) ClearStaged();

        var begun = await _store.BeginAsync(scope, batch, ct).ConfigureAwait(false);
        if (begun.IsFailure) return Result<SyncResult>.Fail(begun.Error, begun.Detail);

        var session = begun.Value!;
        await using (session.ConfigureAwait(false))
        {
            var failure = await StreamPagesAsync(session, batch, ct).ConfigureAwait(false);
            if (failure.Result is null)
            {
                await AbortQuietlyAsync(session).ConfigureAwait(false);
                TraceLog.Error($"SyncEngine.DownloadAndApply — page streaming FAILED ({failure.Error}) detail='{failure.Detail}'.", null, "sync");
                return Result<SyncResult>.Fail(failure.Error, failure.Detail);
            }

            var records = failure.Result.Value;
            var committed = await session.CommitAsync(ct).ConfigureAwait(false);
            if (committed.IsFailure)
            {
                await AbortQuietlyAsync(session).ConfigureAwait(false);
                TraceLog.Error($"SyncEngine.DownloadAndApply — COMMIT FAILED ({committed.Error}) detail='{committed.Detail}' → local checkpoint NOT advanced.", null, "sync");
                return Result<SyncResult>.Fail(committed.Error, committed.Detail);
            }

            ClearStaged();
            TraceLog.Info($"SyncEngine.DownloadAndApply ← Applied {records} record(s); new watermark={committed.Value.Watermark}.", "sync");
            return Result<SyncResult>.Ok(SyncResult.Applied(committed.Value.Watermark, 1, records, _clock.UtcNow));
        }
    }

    private readonly record struct StreamOutcome(int? Result, ErrorCode Error, string? Detail);

    private async Task<StreamOutcome> StreamPagesAsync(ISyncApplySession session, BatchReady batch, CancellationToken ct)
    {
        var pageHashes = new List<string>(batch.PageCount);
        var records = 0;
        try
        {
            for (var ordinal = 0; ordinal < batch.PageCount; ordinal++)
            {
                var fetched = await FetchPageAsync(batch, ordinal, ct).ConfigureAwait(false);
                if (fetched.IsFailure) return new StreamOutcome(null, fetched.Error, fetched.Detail);

                var page = fetched.Value!;
                if (page.PageCount != batch.PageCount)
                    return new StreamOutcome(null, ErrorCodes.SyncProtocolViolation, $"Page {ordinal} reports {page.PageCount} pages, batch has {batch.PageCount}.");

                var accepted = await session.AcceptPageAsync(page, ct).ConfigureAwait(false);
                if (accepted.IsFailure) return new StreamOutcome(null, accepted.Error, accepted.Detail);

                pageHashes.Add(page.PageHash);
                records += page.Records.Count;
            }
        }
        catch (OperationCanceledException)
        {
            // Keep verified pages so the retried pull can resume; the caller's finally aborts the session.
            await AbortQuietlyAsync(session).ConfigureAwait(false);
            throw;
        }

        if (!string.Equals(PageIntegrity.ComputeContentHash(pageHashes), batch.ContentHash, StringComparison.OrdinalIgnoreCase))
        {
            ClearStaged();
            return new StreamOutcome(null, ErrorCodes.SyncIntegrityFailed, $"Batch {batch.BatchId} content hash does not match its pages.");
        }

        return new StreamOutcome(records, ErrorCode.None, null);
    }

    private async Task<Result<SyncPage>> FetchPageAsync(BatchReady batch, int ordinal, CancellationToken ct)
    {
        var attempts = Math.Max(1, _options.MaxPageAttempts);
        for (var attempt = 1; ; attempt++)
        {
            _staged.TryGetValue(ordinal, out var staged);
            var result = await _feed.GetPageAsync(batch.BatchId, ordinal, staged?.PageHash, ct).ConfigureAwait(false);

            if (result.IsSuccess)
            {
                if (result.Value is { } fresh)
                {
                    Stage(batch.BatchId, ordinal, fresh);
                    return Result<SyncPage>.Ok(fresh);
                }

                // 304: the server confirms our staged copy is still the immutable page.
                return staged is not null
                    ? Result<SyncPage>.Ok(staged)
                    : Result<SyncPage>.Fail(ErrorCodes.SyncProtocolViolation, $"Server answered 304 for page {ordinal} that was never staged.");
            }

            var transient = result.Error == ErrorCodes.NetworkUnavailable || result.Error == ErrorCodes.ServerBusy;
            if (!transient || attempt >= attempts)
                return Result<SyncPage>.Fail(result.Error, result.Detail);

            await DelayBeforeRetryAsync(attempt, result, ct).ConfigureAwait(false);
        }
    }

    private async Task DelayBeforeRetryAsync(int attempt, Result<SyncPage?> failed, CancellationToken ct)
    {
        var baseDelay = _options.RetryBaseDelay;
        if (baseDelay <= TimeSpan.Zero) return;

        var exponential = TimeSpan.FromTicks(baseDelay.Ticks * (1L << Math.Min(attempt - 1, 10)));
        var jittered = exponential * (0.5 + Random.Shared.NextDouble() * 0.5);
        if (failed.Error == ErrorCodes.ServerBusy && long.TryParse(failed.Detail, out var retryAfter) && retryAfter > 0)
            jittered = TimeSpan.FromSeconds(retryAfter) > jittered ? TimeSpan.FromSeconds(retryAfter) : jittered;

        await Task.Delay(jittered > MaxBackoff ? MaxBackoff : jittered, _time, ct).ConfigureAwait(false);
    }

    private void Stage(string batchId, int ordinal, SyncPage page)
    {
        if (_stagedBatchId != batchId) ClearStaged();
        _stagedBatchId = batchId;
        if (_staged.Count < MaxStagedPages || _staged.ContainsKey(ordinal)) _staged[ordinal] = page;
    }

    private void ClearStaged()
    {
        _stagedBatchId = null;
        _staged.Clear();
    }

    private static async Task AbortQuietlyAsync(ISyncApplySession session)
    {
        try
        {
            await session.AbortAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Abort is best-effort cleanup; the original failure is what the caller must see.
        }
    }
}
