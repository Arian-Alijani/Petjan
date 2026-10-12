using Animora.Contracts.Wire;
using Animora.Contracts.Wire.Errors;
using Animora.Contracts.Wire.Sync;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
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
            .RequireRateLimiting(RateLimitPolicies.Sync).AddEndpointFilter<SyncTenantBoundFilter>().AddEndpointFilter<SyncContractFilter>();
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
        try
        {
            var started=System.Diagnostics.Stopwatch.GetTimestamp();
            var result=await freezer.OpenAsync(request,full,ct).ConfigureAwait(false);
            var metrics=http.RequestServices.GetService<Animora.Server.Observability.ServerMetrics>();
            metrics?.SyncBatches.Add(1); metrics?.SyncDuration.Record(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return Results.Json(result);
        }
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
        return new SyncPageResult(page, http.RequestServices.GetService<Animora.Server.Observability.ServerMetrics>());
    }
    /// <summary>Counts only gzip bytes successfully written, never 304s, missing pages or promised lengths.</summary>
    internal sealed class SyncPageResult(FeedPageDownload page, Animora.Server.Observability.ServerMetrics? metrics) : IResult
    {
        public async Task ExecuteAsync(HttpContext context)
        {
            await using var content = page.Content;
            context.Response.ContentType = "application/gzip";
            context.Response.ContentLength = page.Length;
            if (HttpMethods.IsHead(context.Request.Method)) return;
            var buffer = new byte[65536];
            int read;
            while ((read = await content.ReadAsync(buffer, context.RequestAborted).ConfigureAwait(false)) > 0)
            {
                await context.Response.Body.WriteAsync(buffer.AsMemory(0, read), context.RequestAborted).ConfigureAwait(false);
                metrics?.SyncBytes.Add(read);
            }
        }
    }
}

/// <summary>
/// Defence in depth behind the <c>TenantMember</c>/<c>DeviceActive</c> policies: a sync request that reaches the
/// handler without a tenant bound by the tenant middleware fails closed as a typed <c>401 SESSION_EXPIRED</c>
/// instead of letting the tenant-filtered DbContext throw (which the global handler would render as a 500).
/// Runs before <see cref="SyncContractFilter"/> and before any body-driven or tenant-scoped work.
/// </summary>
internal sealed class SyncTenantBoundFilter : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var tenant = http.RequestServices.GetService<ITenantContext>();
        if (tenant?.TenantId is { Length: > 0 }) return next(context);
        var correlationId = CorrelationHeader.Resolve(http);
        CorrelationHeader.Echo(http, correlationId);
        return ValueTask.FromResult<object?>(Results.Json(
            new WireApiError(ErrorCodes.SessionExpired.Code, null, [], correlationId),
            statusCode: StatusCodes.Status401Unauthorized));
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
