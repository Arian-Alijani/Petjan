using Animora.Server.Authorization;
using Animora.Server.Hosting;
using Animora.Server.Provisioning;
namespace Animora.Server.Operator;

internal sealed record WebsiteClientRequest(string Label);
internal static class WebsiteClientEndpoints
{
    public static IEndpointRouteBuilder MapWebsiteClientEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(ServerRoutes.Operator + "/website-clients").RequireAuthorization(CoarsePolicies.OperatorOnly).RequireRateLimiting(RateLimitPolicies.Operator);
        group.MapPost("", async (WebsiteClientRequest r, WebsiteClientService service, HttpContext http, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            var result = await service.CreateAsync(r.Label, OperatorEndpoints.Actor(http), ct).ConfigureAwait(false);
            return result.IsSuccess ? Results.Ok(result.Value) : OperatorEndpoints.Failure(result.Error, http);
        });
        group.MapPost("/{id}/revoke", async (string id, WebsiteClientService service, HttpContext http, CancellationToken ct) =>
            OperatorEndpoints.ToHttp(await service.RevokeAsync(id, OperatorEndpoints.Actor(http), ct).ConfigureAwait(false), http));
        return app;
    }
}
