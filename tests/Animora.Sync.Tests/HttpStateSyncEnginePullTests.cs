using System.Net;
using Animora.Contracts.Wire.Sync;
using Animora.Domain.Results;
using Animora.Sync.Abstractions;
using Animora.Sync.Custom;
using Xunit;

namespace Animora.Sync.Tests;

/// <summary>Behavioural facts for <see cref="HttpStateSyncEngine"/> against a scripted feed server.</summary>
public sealed class HttpStateSyncEnginePullTests
{
    private const long Epoch = 7;
    private static readonly FixedClock Clock = new(FeedFixtures.Now);

    /// <summary>A scripted immutable batch: gzip pages, ETag = gzip hash, content hash = server rule.</summary>
    private sealed class FeedScript
    {
        public byte[][] Pages { get; }
        public BatchReady Batch { get; }
        public Dictionary<int, Queue<Func<HttpResponseMessage>>> PageFaults { get; } = [];
        public List<string> IfNoneMatch { get; } = [];
        public int OpenCalls { get; private set; }
        public OpenBatchResponse? OpenOverride { get; set; }

        public FeedScript(int pages, int recordsPerPage = 2, long watermark = 40)
        {
            Pages = Enumerable.Range(0, pages)
                .Select(i => FeedFixtures.Gzip(FeedFixtures.Page("b1", i, pages, recordsPerPage))).ToArray();
            var hashes = Pages.Select(p => PageIntegrity.ComputePageHash(p)).ToList();
            Batch = new BatchReady("b1", Epoch, 1, 0, watermark, pages, PageIntegrity.ComputeContentHash(hashes));
        }

        public FakeFeedHandler Handler() => new(Respond);

        private HttpResponseMessage Respond(HttpRequestMessage request)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/sync/manifest")
                return FeedFixtures.JsonResponse(new SyncManifest(2, Epoch, 1, ["Pet"], 262144));
            if (path == "/sync/batches")
            {
                OpenCalls++;
                return FeedFixtures.JsonResponse(OpenOverride ?? new OpenBatchResponse("BatchReady", Batch: Batch));
            }

            var ordinal = int.Parse(path[(path.LastIndexOf('/') + 1)..]);
            if (request.Headers.IfNoneMatch.Count > 0) IfNoneMatch.Add(request.Headers.IfNoneMatch.Single().Tag.Trim('"'));
            if (PageFaults.TryGetValue(ordinal, out var faults) && faults.Count > 0) return faults.Dequeue()();
            if (request.Headers.IfNoneMatch.Count > 0 &&
                request.Headers.IfNoneMatch.Single().Tag.Trim('"') == PageIntegrity.ComputePageHash(Pages[ordinal]))
                return new HttpResponseMessage(HttpStatusCode.NotModified);
            return FeedFixtures.PageResponse(Pages[ordinal]);
        }
    }

    private static HttpStateSyncEngine Engine(FeedScript feed, out BufferingSyncApplyStore store, out FakeFeedHandler handler,
        ISyncApplyStore? overrideStore = null, Action<ServerEndpointOptions>? configure = null)
    {
        var options = FeedFixtures.Options();
        configure?.Invoke(options);
        handler = feed.Handler();
        store = new BufferingSyncApplyStore();
        var client = new SyncFeedClient(new HttpClient(handler) { BaseAddress = new Uri("https://sync.test/") }, options);
        return new HttpStateSyncEngine(client, overrideStore ?? store, options, Clock);
    }

    [Fact]
    public async Task UpToDate_ReturnsUpToDate()
    {
        var feed = new FeedScript(1) { OpenOverride = new OpenBatchResponse("UpToDate", Current: new UpToDate(40)) };
        var engine = Engine(feed, out _, out var handler);

        var result = await engine.PullAsync(SyncScope.Default);

        Assert.Equal(SyncOutcome.UpToDate, result.Value!.Outcome);
        Assert.Equal(40, result.Value.Watermark);
        Assert.DoesNotContain(handler.Requests, r => r.Contains("/pages/"));
    }

    [Fact]
    public async Task Ready_DownloadsAppliesAll_ReturnsApplied()
    {
        var feed = new FeedScript(3, recordsPerPage: 4);
        var engine = Engine(feed, out var store, out var handler);

        var result = await engine.PullAsync(SyncScope.Default);

        Assert.True(result.IsSuccess);
        Assert.Equal(SyncOutcome.Applied, result.Value!.Outcome);
        Assert.Equal(40, result.Value.Watermark);
        Assert.Equal(1, result.Value.BatchesApplied);
        Assert.Equal(12, result.Value.RecordsApplied);
        Assert.Equal(3, handler.Requests.Count(r => r.Contains("/pages/")));
        var checkpoint = (await store.ReadCheckpointAsync(SyncScope.Default)).Value;
        Assert.Equal(new SyncCheckpoint(Epoch, 40), checkpoint);
    }

    [Fact]
    public async Task SecondPull_StartsFromCheckpoint()
    {
        var feed = new FeedScript(1);
        var engine = Engine(feed, out _, out var handler);
        await engine.PullAsync(SyncScope.Default);
        feed.OpenOverride = new OpenBatchResponse("UpToDate", Current: new UpToDate(40));

        var second = await engine.PullAsync(SyncScope.Default);

        Assert.Equal(SyncOutcome.UpToDate, second.Value!.Outcome);
        Assert.Equal(2, feed.OpenCalls);
    }

    [Fact]
    public async Task Resume_304Page_ReusesStaged()
    {
        var feed = new FeedScript(2);
        // Page 1 fails permanently on the first pull (404 → not transient), leaving page 0 staged.
        feed.PageFaults[1] = new Queue<Func<HttpResponseMessage>>([() => new HttpResponseMessage(HttpStatusCode.NotFound)]);
        var engine = Engine(feed, out var store, out _);

        var first = await engine.PullAsync(SyncScope.Default);
        Assert.Equal(ErrorCodes.BatchNotFound, first.Error);
        Assert.Equal(default, (await store.ReadCheckpointAsync(SyncScope.Default)).Value);

        var second = await engine.PullAsync(SyncScope.Default);

        Assert.Equal(SyncOutcome.Applied, second.Value!.Outcome);
        Assert.Equal(PageIntegrity.ComputePageHash(feed.Pages[0]), Assert.Single(feed.IfNoneMatch));
        Assert.Equal(new SyncCheckpoint(Epoch, 40), (await store.ReadCheckpointAsync(SyncScope.Default)).Value);
    }

    [Fact]
    public async Task TransientFault_IsRetriedThenApplied()
    {
        var feed = new FeedScript(1);
        feed.PageFaults[0] = new Queue<Func<HttpResponseMessage>>(
        [
            () => throw new HttpRequestException("blip"),
            () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
        ]);
        var engine = Engine(feed, out _, out var handler);

        var result = await engine.PullAsync(SyncScope.Default);

        Assert.Equal(SyncOutcome.Applied, result.Value!.Outcome);
        Assert.Equal(3, handler.Requests.Count(r => r.Contains("/pages/")));
    }

    [Fact]
    public async Task TransientFault_ExhaustsAttempts_FailsWithoutCheckpointAdvance()
    {
        var feed = new FeedScript(1);
        feed.PageFaults[0] = new Queue<Func<HttpResponseMessage>>(Enumerable.Repeat<Func<HttpResponseMessage>>(
            () => throw new HttpRequestException("down"), 10));
        var engine = Engine(feed, out var store, out _);

        var result = await engine.PullAsync(SyncScope.Default);

        Assert.Equal(ErrorCodes.NetworkUnavailable, result.Error);
        Assert.Equal(default, (await store.ReadCheckpointAsync(SyncScope.Default)).Value);
    }

    [Fact]
    public async Task IntegrityMismatch_AbortsSession_NoCheckpointAdvance()
    {
        var feed = new FeedScript(2);
        var tampered = new FeedScript(2);
        // Server lies about the batch content hash → every page verifies, but the batch does not.
        feed.OpenOverride = new OpenBatchResponse("BatchReady", Batch: feed.Batch with { ContentHash = tampered.Batch.ContentHash.Replace('a', 'b').Replace('0', '1') + "x" });
        var recording = new RecordingStore();
        var engine = Engine(feed, out _, out _, overrideStore: recording);

        var result = await engine.PullAsync(SyncScope.Default);

        Assert.Equal(ErrorCodes.SyncIntegrityFailed, result.Error);
        Assert.True(recording.Aborted);
        Assert.False(recording.Committed);
        Assert.Equal(default, (await recording.ReadCheckpointAsync(SyncScope.Default)).Value);
    }

    [Fact]
    public async Task Resync_ReturnsResyncRequired()
    {
        var feed = new FeedScript(1) { OpenOverride = new OpenBatchResponse("ResyncRequired", Resync: new ResyncRequired("BEYOND_RETENTION", Epoch)) };
        var engine = Engine(feed, out _, out _);

        var result = await engine.PullAsync(SyncScope.Default);

        Assert.Equal(SyncOutcome.ResyncRequired, result.Value!.Outcome);
        Assert.Equal("BEYOND_RETENTION", result.Value.Detail);
    }

    [Fact]
    public async Task EpochChange_ReturnsResyncRequired_WithoutOpeningBatch()
    {
        var feed = new FeedScript(1);
        var store = new RecordingStore { Checkpoint = new SyncCheckpoint(Epoch - 1, 10) };
        var engine = Engine(feed, out _, out _, overrideStore: store);

        var result = await engine.PullAsync(SyncScope.Default);

        Assert.Equal(SyncOutcome.ResyncRequired, result.Value!.Outcome);
        Assert.Equal("EPOCH_CHANGED", result.Value.Detail);
        Assert.Equal(0, feed.OpenCalls);
    }

    [Fact]
    public async Task OversizedBatch_IsProtocolViolation()
    {
        var feed = new FeedScript(3);
        var engine = Engine(feed, out _, out _, configure: o => o.MaxBatchPages = 2);

        var result = await engine.PullAsync(SyncScope.Default);

        Assert.Equal(ErrorCodes.SyncProtocolViolation, result.Error);
    }

    [Fact]
    public async Task Unconfigured_ReturnsDeferred()
    {
        var feed = new FeedScript(1);
        var options = new ServerEndpointOptions { BaseAddress = null };
        var handler = feed.Handler();
        var client = new SyncFeedClient(new HttpClient(handler) { BaseAddress = new Uri("https://sync.test/") }, options);
        var engine = new HttpStateSyncEngine(client, new BufferingSyncApplyStore(), options, Clock);

        var result = await engine.PullAsync(SyncScope.Default);

        Assert.False(engine.IsConfigured);
        Assert.Equal(SyncOutcome.Deferred, result.Value!.Outcome);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task CallerCancellation_Surfaces_AndAbortsSession()
    {
        var feed = new FeedScript(2);
        var recording = new RecordingStore();
        using var cts = new CancellationTokenSource();
        feed.PageFaults[1] = new Queue<Func<HttpResponseMessage>>([() => { cts.Cancel(); throw new OperationCanceledException(cts.Token); }]);
        var engine = Engine(feed, out _, out _, overrideStore: recording);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.PullAsync(SyncScope.Default, cts.Token));

        Assert.True(recording.Aborted);
        Assert.False(recording.Committed);
    }

    /// <summary>An apply store that records session outcome and can be pre-seeded with a checkpoint.</summary>
    private sealed class RecordingStore : ISyncApplyStore
    {
        public SyncCheckpoint Checkpoint { get; set; }
        public bool Aborted { get; private set; }
        public bool Committed { get; private set; }

        public Task<Result<SyncCheckpoint>> ReadCheckpointAsync(SyncScope scope, CancellationToken ct = default) =>
            Task.FromResult(Result<SyncCheckpoint>.Ok(Checkpoint));

        public Task<Result<ISyncApplySession>> BeginAsync(SyncScope scope, BatchReady batch, CancellationToken ct = default) =>
            Task.FromResult(Result<ISyncApplySession>.Ok(new Session(this, batch)));

        private sealed class Session(RecordingStore owner, BatchReady batch) : ISyncApplySession
        {
            public Task<Result> AcceptPageAsync(SyncPage page, CancellationToken ct = default) => Task.FromResult(Result.Ok());

            public Task<Result<SyncCheckpoint>> CommitAsync(CancellationToken ct = default)
            {
                owner.Committed = true;
                owner.Checkpoint = new SyncCheckpoint(batch.Epoch, batch.Watermark);
                return Task.FromResult(Result<SyncCheckpoint>.Ok(owner.Checkpoint));
            }

            public Task<Result> AbortAsync(CancellationToken ct = default)
            {
                owner.Aborted = true;
                return Task.FromResult(Result.Ok());
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
