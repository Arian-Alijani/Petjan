using System.Net;
using System.Net.Http.Headers;
using Animora.Contracts.Wire.Sync;
using Animora.Domain.Results;
using Animora.Sync.Custom;
using Xunit;

namespace Animora.Sync.Tests;

/// <summary>Facts for the HTTP-to-<see cref="Result"/> mapping of <see cref="SyncFeedClient"/>.</summary>
public sealed class SyncFeedClientTests
{
    private static HttpResponseMessage Status(HttpStatusCode code) => new(code);

    [Fact]
    public async Task Manifest_Returns_SyncManifest()
    {
        var manifest = new SyncManifest(2, 7, 3, ["Pet", "Owner"], 262144);
        var handler = new FakeFeedHandler(_ => FeedFixtures.JsonResponse(manifest));
        var result = await FeedFixtures.Client(handler).GetManifestAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(7, result.Value!.Epoch);
        Assert.Equal(["Pet", "Owner"], result.Value.ProjectionOrder);
        Assert.Equal(["GET /sync/manifest"], handler.Requests);
    }

    [Fact]
    public async Task Open_MapsUpToDate_Ready_Resync()
    {
        var ready = new BatchReady("b1", 7, 3, 0, 50, 2, "abc");
        var queue = new Queue<OpenBatchResponse>(
        [
            new("UpToDate", Current: new UpToDate(50)),
            new("BatchReady", Batch: ready),
            new("ResyncRequired", Resync: new ResyncRequired("BEYOND_RETENTION", 7)),
        ]);
        var client = FeedFixtures.Client(new FakeFeedHandler(_ => FeedFixtures.JsonResponse(queue.Dequeue())));

        var current = (await client.OpenBatchAsync(7, 50)).Value!;
        Assert.Equal(OpenBatchKind.Current, current.Kind);
        Assert.Equal(50, current.Watermark);

        var batch = (await client.OpenBatchAsync(7, 0)).Value!;
        Assert.Equal(OpenBatchKind.Ready, batch.Kind);
        Assert.Equal(ready, batch.Batch);

        var resync = (await client.OpenBatchAsync(7, 0)).Value!;
        Assert.Equal(OpenBatchKind.Resync, resync.Kind);
        Assert.Equal("BEYOND_RETENTION", resync.Reason);
    }

    [Fact]
    public async Task Open_SendsContractHeaderAndBody()
    {
        string? header = null;
        string? body = null;
        var handler = new FakeFeedHandler(request =>
        {
            header = request.Headers.GetValues("X-Animora-Contract-Version").Single();
            body = request.Content!.ReadAsStringAsync().Result;
            return FeedFixtures.JsonResponse(new OpenBatchResponse("UpToDate", Current: new UpToDate(1)));
        });
        await FeedFixtures.Client(handler).OpenBatchAsync(5, 9);

        Assert.Equal("2", header);
        Assert.Equal("{\"epoch\":5,\"fromSequence\":9}", body);
    }

    [Fact]
    public async Task Page_304_ReturnsNullOk()
    {
        var handler = new FakeFeedHandler(request =>
        {
            Assert.Equal("\"abc\"", request.Headers.IfNoneMatch.Single().Tag);
            return Status(HttpStatusCode.NotModified);
        });
        var result = await FeedFixtures.Client(handler).GetPageAsync("b1", 0, "abc");

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task Page_200_VerifiesHashDecompressesAndStampsPageHash()
    {
        var gzip = FeedFixtures.Gzip(FeedFixtures.Page("b1", 0, 1, 3));
        var result = await FeedFixtures.Client(new FakeFeedHandler(_ => FeedFixtures.PageResponse(gzip))).GetPageAsync("b1", 0, null);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.Records.Count);
        Assert.Equal(PageIntegrity.ComputePageHash(gzip), result.Value.PageHash);
    }

    [Fact]
    public async Task Page_EtagMismatch_FailsIntegrity()
    {
        var gzip = FeedFixtures.Gzip(FeedFixtures.Page("b1", 0, 1, 1));
        var handler = new FakeFeedHandler(_ =>
        {
            var response = FeedFixtures.PageResponse(gzip);
            response.Headers.ETag = new EntityTagHeaderValue("\"deadbeef\"");
            return response;
        });
        var result = await FeedFixtures.Client(handler).GetPageAsync("b1", 0, null);

        Assert.Equal(ErrorCodes.SyncIntegrityFailed, result.Error);
    }

    [Fact]
    public async Task Page_OversizedBody_IsProtocolViolation()
    {
        var gzip = FeedFixtures.Gzip(FeedFixtures.Page("b1", 0, 1, 50));
        var client = FeedFixtures.Client(new FakeFeedHandler(_ => FeedFixtures.PageResponse(gzip)), o => o.MaxPageBytes = 16);
        var result = await client.GetPageAsync("b1", 0, null);

        Assert.Equal(ErrorCodes.SyncProtocolViolation, result.Error);
    }

    [Fact]
    public async Task Page_WrongOrdinalInBody_IsProtocolViolation()
    {
        var gzip = FeedFixtures.Gzip(FeedFixtures.Page("b1", 5, 8, 1));
        var result = await FeedFixtures.Client(new FakeFeedHandler(_ => FeedFixtures.PageResponse(gzip))).GetPageAsync("b1", 0, null);

        Assert.Equal(ErrorCodes.SyncProtocolViolation, result.Error);
    }

    [Fact]
    public async Task Status404_MapsBatchNotFound()
    {
        var result = await FeedFixtures.Client(new FakeFeedHandler(_ => Status(HttpStatusCode.NotFound))).GetPageAsync("gone", 0, null);
        Assert.Equal(ErrorCodes.BatchNotFound, result.Error);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Status401And403_MapUnauthorized(HttpStatusCode code)
    {
        var result = await FeedFixtures.Client(new FakeFeedHandler(_ => Status(code))).GetManifestAsync();
        Assert.Equal(ErrorCodes.Unauthorized, result.Error);
    }

    [Fact]
    public async Task Status426_MapsResyncContractUnsupported()
    {
        var handler = new FakeFeedHandler(_ => FeedFixtures.JsonResponse(new ResyncRequired("CONTRACT_UNSUPPORTED", 0), HttpStatusCode.UpgradeRequired));
        var result = await FeedFixtures.Client(handler).OpenBatchAsync(7, 0);

        Assert.True(result.IsSuccess);
        Assert.Equal(OpenBatchKind.Resync, result.Value!.Kind);
        Assert.Equal("CONTRACT_UNSUPPORTED", result.Value.Reason);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Status503_WithRetryAfter_MapsServerBusy(HttpStatusCode code)
    {
        var handler = new FakeFeedHandler(_ =>
        {
            var response = Status(code);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
            return response;
        });
        var result = await FeedFixtures.Client(handler).GetManifestAsync();

        Assert.Equal(ErrorCodes.ServerBusy, result.Error);
        Assert.Equal("1", result.Detail);
    }

    [Fact]
    public async Task NetworkFault_MapsNetworkUnavailable()
    {
        var handler = new FakeFeedHandler(_ => throw new HttpRequestException("boom"));
        var result = await FeedFixtures.Client(handler).GetManifestAsync();

        Assert.Equal(ErrorCodes.NetworkUnavailable, result.Error);
    }

    [Fact]
    public async Task CallerCancellation_Surfaces_NotMappedToNetworkError()
    {
        var handler = new FakeFeedHandler(_ => throw new OperationCanceledException());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => FeedFixtures.Client(handler).GetManifestAsync(cts.Token));
    }
}
