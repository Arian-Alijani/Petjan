using Animora.Domain.Time;
using Animora.Persistence.PostgreSql.Provisioning;
using Animora.Server.Authorization;
using Animora.Server.Hosting;
namespace Animora.Server.Operator;

internal static class ProvisioningIntentEndpoints
{
    public static IEndpointRouteBuilder MapOperatorProvisioningEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(ServerRoutes.Operator + "/provisioning/intents").RequireAuthorization(CoarsePolicies.OperatorOnly).RequireRateLimiting(RateLimitPolicies.Operator);
        group.MapGet("", async (IProvisioningIntentStore store, HttpContext http, CancellationToken ct) =>
        {
            ProvisioningIntentStatus? status = Enum.TryParse<ProvisioningIntentStatus>(http.Request.Query["status"], true, out var parsed) ? parsed : null;
            return Results.Ok(await store.ListAsync(status, OperatorEndpoints.Number(http, "page", 1, int.MaxValue / 200),
                OperatorEndpoints.Number(http, "pageSize", 50, 200), ct).ConfigureAwait(false));
        });
        group.MapPost("/{id}/retry", async (string id, IProvisioningIntentStore store, IClock clock, HttpContext http, CancellationToken ct) =>
            OperatorEndpoints.ToHttp(await store.RetryAsync(id, OperatorEndpoints.Actor(http), clock.UtcNow, ct).ConfigureAwait(false), http));
        return app;
    }
}
