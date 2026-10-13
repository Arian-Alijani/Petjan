using System.Globalization;
using System.Threading.RateLimiting;
using Animora.Contracts.Wire.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Animora.Server.Hosting;

/// <summary>
/// Configures the five named fixed-window rate-limit policies (ARCHITECTURE.md
/// §4). Windows are options-driven (<see cref="ServerOptions.RateLimits"/>); a
/// rejection becomes a 429 <see cref="WireApiError"/> carrying <c>Retry-After</c>.
/// Health endpoints carry no policy and are therefore never limited.
/// </summary>
internal static class ServerRateLimiting
{
    /// <summary>Registers rejection handling and one partitioned limiter per policy.</summary>
    public static void Configure(RateLimiterOptions limiter)
    {
        ArgumentNullException.ThrowIfNull(limiter);

        limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        limiter.OnRejected = async (context, token) =>
        {
            var httpContext = context.HttpContext;
            httpContext.RequestServices.GetService<Animora.Server.Observability.ServerMetrics>()?.RateLimitRejected.Add(1);
            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            {
                httpContext.Response.Headers.RetryAfter =
                    ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
            }

            var correlationId = CorrelationHeader.Resolve(httpContext);
            CorrelationHeader.Echo(httpContext, correlationId);

            var error = new WireApiError(
                Code: ServerErrorCodes.RateLimited,
                Detail: null,
                Fields: [],
                CorrelationId: correlationId);
            await httpContext.Response.WriteAsJsonAsync(error, token).ConfigureAwait(false);
        };

        foreach (var policy in RateLimitPolicies.All)
        {
            var policyName = policy;
            limiter.AddPolicy(policyName, httpContext =>
            {
                var options = httpContext.RequestServices.GetRequiredService<IOptions<ServerOptions>>().Value;
                var window = options.RateLimits.ForPolicy(policyName);

                // SEC-RL-01 / CONS-RL-01: authenticated planes (commands/sync/files/operator) are
                // partitioned by tenant+user so one tenant/user cannot be throttled by another sharing
                // a NAT/proxy IP, and a single abusive user is isolated. The unauthenticated `auth`
                // plane (login/refresh/device) is partitioned by the *trusted* client IP derived from
                // the forwarded-headers allowlist (SEC-FWD-01) so brute-force protection stays effective.
                var partitionKey = ResolvePartitionKey(policyName, httpContext);
                return RateLimitPartition.GetFixedWindowLimiter(
                    $"{policyName}:{partitionKey}",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = window.PermitLimit,
                        Window = TimeSpan.FromSeconds(window.WindowSeconds),
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    });
            });
        }
    }

    /// <summary>
    /// Chooses the partition key for a policy. Authenticated planes key on tenant+user (falling back to
    /// the trusted client IP only before the principal exists); the auth plane always keys on the
    /// trusted client IP. The forwarded-headers middleware (SEC-FWD-01) has already normalized
    /// <see cref="ConnectionInfo.RemoteIpAddress"/> to the real client when a proxy allowlist is configured.
    /// </summary>
    private static string ResolvePartitionKey(string policyName, HttpContext httpContext)
    {
        // The auth plane is inherently pre-authentication → IP is the only stable key.
        if (policyName != RateLimitPolicies.Auth)
        {
            var caller = Animora.Server.Auth.AuthenticatedCaller.TryRead(httpContext.User);
            if (caller is not null)
            {
                return $"t:{caller.TenantId}:u:{caller.UserId}";
            }
        }

        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return $"ip:{ip}";
    }
}
