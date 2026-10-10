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

                // S01 partitions every policy by client IP. Tenant-scoped partitioning
                // for commands/sync/files/operator arrives with tenant context (S04/S05).
                var partitionKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
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
}
