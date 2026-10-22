using System.Globalization;
using Animora.Application.Ports;
using Animora.Contracts.Wire.Errors;
using Animora.Contracts.Wire.Sync;
using Animora.Domain.Results;
using Animora.Server.Authorization;
using Animora.Server.Hosting;

namespace Animora.Server.Sync;

/// <summary>
/// Vendor-message delivery. <c>GET /notifications?since={ticks}&amp;take={n}</c> hands a desktop device the
/// admin-authored messages addressed to its signed-in user (the generic state feed deliberately excludes the
/// vendor-global <c>TenantNotification</c> rows), and <c>POST /notifications/receipts</c> records which messages the
/// device actually stored so the admin panel can report REAL delivery. Read-mostly side channel: nothing here
/// crosses into the command pipeline.
/// <para>Gated like the sync feed (<see cref="CoarsePolicies.TenantMember"/> + <see cref="CoarsePolicies.DeviceActive"/>),
/// rate-limited with the sync policy, and guarded by <see cref="SyncTenantBoundFilter"/>.</para>
/// </summary>
internal static class NotificationFeedEndpoints
{
    private const int DefaultTake = 100;
    private const string LogCategory = "Animora.Server.Sync.Notifications";

    public static IEndpointRouteBuilder MapNotificationFeedEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup(ServerRoutes.Notifications)
            .RequireAuthorization(CoarsePolicies.TenantMember, CoarsePolicies.DeviceActive)
            .RequireRateLimiting(RateLimitPolicies.Sync)
            .AddEndpointFilter<SyncTenantBoundFilter>();

        group.MapGet("/", FeedAsync);
        group.MapPost("/receipts", ReceiptsAsync);
        return endpoints;
    }

    internal static async Task<IResult> FeedAsync(
        HttpContext http, IRequestAuthorizationState state, INotificationFeedStore store, CancellationToken ct)
    {
        if (state.Membership is not { } member) return Unauthorized(http);

        long since = 0;
        if (http.Request.Query.TryGetValue("since", out var sinceRaw)
            && long.TryParse(sinceRaw.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedSince)
            && parsedSince > 0)
        {
            since = parsedSince;
        }

        int take = DefaultTake;
        if (http.Request.Query.TryGetValue("take", out var takeRaw)
            && int.TryParse(takeRaw.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedTake)
            && parsedTake > 0)
        {
            take = parsedTake;
        }

        try
        {
            var page = await store.GetFeedAsync(since, take, member.UserId, member.Role.ToString(), ct).ConfigureAwait(false);
            return Results.Json(page);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log(http, ex, "Notification feed read");
            throw;
        }
    }

    internal static async Task<IResult> ReceiptsAsync(
        HttpContext http, NotificationReceiptReport report, IRequestAuthorizationState state,
        INotificationFeedStore store, CancellationToken ct)
    {
        if (state.Membership is not { } member) return Unauthorized(http);
        if (report?.NotificationIds is null) return Results.BadRequest();

        try
        {
            var recorded = await store.RecordReceiptsAsync(member.DeviceId, member.UserId, report.NotificationIds, ct).ConfigureAwait(false);
            return Results.Json(new { recorded });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log(http, ex, "Notification receipt write");
            throw;
        }
    }

    private static IResult Unauthorized(HttpContext http)
    {
        var correlationId = CorrelationHeader.Resolve(http);
        CorrelationHeader.Echo(http, correlationId);
        return Results.Json(
            new WireApiError(ErrorCodes.SessionExpired.Code, null, [], correlationId, Category: ErrorCodes.SessionExpired.Category),
            statusCode: StatusCodes.Status401Unauthorized);
    }

    private static void Log(HttpContext http, Exception ex, string what) =>
        http.RequestServices.GetService<ILoggerFactory>()?.CreateLogger(LogCategory)
            .LogError(ex, "{Operation} failed ({FailureClass}) for request {CorrelationId}.",
                what, ex.GetType().FullName, CorrelationHeader.Resolve(http));
}
