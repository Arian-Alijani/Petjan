using Animora.Contracts.Wire;
using Animora.Contracts.Wire.Sync;
using Animora.Domain.Primitives;
using Animora.Persistence.PostgreSql.Identity;
using Animora.Persistence.PostgreSql.Replication;
using Animora.Persistence.PostgreSql.Tenancy;
using Animora.Server.Authorization;
using Animora.Server.Hosting;
namespace Animora.Server.Sync;

internal static class SyncEndpoints
{
    public static IEndpointRouteBuilder MapSyncEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(ServerRoutes.Sync).RequireAuthorization(CoarsePolicies.TenantMember, CoarsePolicies.DeviceActive)
            .RequireRateLimiting(RateLimitPolicies.Sync).AddEndpointFilter<SyncContractFilter>();
        group.MapGet("/manifest", async (IFeedContextFactory factory, CancellationToken ct) =>
        {
            await using var db = factory.Create();
            var p = await new FeedPositionReader(db).GetAsync(db.CurrentTenantId, ct).ConfigureAwait(false);
            return Results.Json(new SyncManifest(WireContract.Version, p.Epoch, p.Generation,
                SyncContractCatalog.ProjectionOrder.Select(d => d.Name).ToArray(), FeedStorageOptions.PageTargetBytes));
        });
        group.MapPost("/batches", async (OpenBatchRequest request, HttpContext http, BatchFreezer freezer, CancellationToken ct) =>
            await Open(http, freezer, request, false, ct).ConfigureAwait(false));
        group.MapPost("/snapshots", async (HttpContext http, BatchFreezer freezer, CancellationToken ct) =>
            await Open(http, freezer, new(0, 0), true, ct).ConfigureAwait(false));
        group.MapGet("/batches/{batchId}/pages/{ordinal:int}", Download);
        return endpoints;
    }
    private static async Task<IResult> Open(HttpContext http, BatchFreezer freezer, OpenBatchRequest request, bool full, CancellationToken ct)
    {
        if (!full && (request.Epoch < 1 || request.FromSequence < 0)) return Results.BadRequest();
        try { return Results.Json(await freezer.OpenAsync(request, full, ct).ConfigureAwait(false)); }
        catch (SnapshotLifetimeExceededException)
        {
            http.Response.Headers.RetryAfter = "1";
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }
    internal static async Task<IResult> Download(string batchId, int ordinal, HttpContext http,
        FeedPageReader reader, ITenantContext tenant, ISecurityAuditWriter audit, CancellationToken ct)
    {
        var page = await reader.OpenAsync(batchId, ordinal, ct).ConfigureAwait(false);
        if (page is null)
        {
            var now = DateTimeOffset.UtcNow;
            var result = await audit.WriteAsync(SecurityAuditEntry.Create(IdGenerator.NewId(now), tenant.TenantId!, now,
                "sync", "sync.batch.not_found", "failure", CorrelationHeader.Resolve(http)), ct).ConfigureAwait(false);
            return result.IsSuccess ? Results.NotFound() : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        string etag = "\"" + page.Hash + "\"";
        http.Response.Headers.ETag = etag;
        http.Response.Headers.CacheControl = "private, immutable";
        http.Response.Headers.Vary = "Authorization, " + WireContract.HeaderName;
        if (http.Request.Headers.IfNoneMatch.ToString().Split(',').Select(x => x.Trim()).Any(x => x == etag || x == "W/" + etag || x == "*"))
        {
            await page.Content.DisposeAsync().ConfigureAwait(false);
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }
        // Representation is the immutable gzip byte stream (not response middleware recompression).
        return Results.Stream(page.Content, "application/gzip", enableRangeProcessing: false);
    }
}

internal sealed class SyncContractFilter : IEndpointFilter
{
    internal static bool Supported(HttpRequest request) => request.Headers[WireContract.HeaderName].Count == 1 &&
        request.Headers[WireContract.HeaderName][0] == "2";
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (Supported(context.HttpContext.Request)) return next(context);
        context.HttpContext.Response.Headers[WireContract.SupportedVersionsHeaderName] = "1,2";
        return ValueTask.FromResult<object?>(Results.Json(new ResyncRequired("CONTRACT_UNSUPPORTED", 0),
            statusCode: StatusCodes.Status426UpgradeRequired));
    }
}
