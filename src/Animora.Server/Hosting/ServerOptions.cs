namespace Animora.Server.Hosting;

/// <summary>
/// Bound from the <c>Server</c> configuration section (ARCHITECTURE.md §4).
/// Validated at boot by <see cref="ServerOptionsValidator"/> with
/// <c>ValidateOnStart</c>, so a misconfiguration fails the process at startup
/// rather than on the first request.
/// </summary>
internal sealed class ServerOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Server";

    /// <summary>
    /// Absolute path to the server's data root. Required; its writability is a
    /// readiness condition (<see cref="DataRootWritableProbe"/>). Supplied via
    /// environment (<c>Server__DataRootPath</c>), never a secret.
    /// </summary>
    public string DataRootPath { get; set; } = string.Empty;

    /// <summary>Expose first-party OpenAPI even outside Development. Default false.</summary>
    public bool EnableOpenApi { get; set; }

    /// <summary>Optional public base URL (absolute) used to build external links.</summary>
    public string? PublicBaseUrl { get; set; }

    /// <summary>The five named rate-limit windows (ARCHITECTURE.md §4).</summary>
    public RateLimitOptions RateLimits { get; set; } = new();

    /// <summary>Trusted-proxy / forwarded-headers configuration (SEC-FWD-01).</summary>
    public ForwardedHeadersSettings ForwardedHeaders { get; set; } = new();

    /// <summary>Reporting query admission (PERF-QB-01).</summary>
    public QueryBudgetSettings QueryBudget { get; set; } = new();

    /// <summary>Short-TTL authorization/entitlement verdict cache (PERF-AUTHZ-01).</summary>
    public AuthCacheSettings AuthCache { get; set; } = new();
}

/// <summary>PERF-QB-01: reporting-query admission bounds.</summary>
internal sealed class QueryBudgetSettings
{
    /// <summary>Concurrent reporting queries per instance; 0 = size to the worker pool (<c>Database:WorkerMax</c>).</summary>
    public int ReportingMax { get; set; }

    /// <summary>How long a report waits for a permit before failing with 503 + Retry-After. Default 10.</summary>
    public int WaitSeconds { get; set; } = 10;
}

/// <summary>
/// PERF-AUTHZ-01: bounds of the per-process authZ/entitlement verdict cache. A cached positive verdict can
/// lag a tenant suspension / licence revocation by at most <see cref="TtlSeconds"/>; <c>0</c> disables caching
/// (every request re-reads the store).
/// </summary>
internal sealed class AuthCacheSettings
{
    /// <summary>Absolute lifetime of a cached positive verdict, 0..60 seconds. Default 10.</summary>
    public int TtlSeconds { get; set; } = 10;

    /// <summary>Upper bound of cached entries per map (memory bound). Default 10 000.</summary>
    public int MaxEntries { get; set; } = 10_000;
}

/// <summary>
/// SEC-FWD-01: controls how the client IP / scheme are derived from
/// <c>X-Forwarded-For</c> / <c>X-Forwarded-Proto</c>. Default is <b>fail-safe</b>:
/// <see cref="Enabled"/> is <c>false</c>, so no forwarded header is trusted and the
/// connection's own peer address is used. Behind a reverse proxy / load balancer the
/// operator MUST set <see cref="Enabled"/> = true and list the proxy addresses in
/// <see cref="KnownProxies"/> / <see cref="KnownNetworks"/>; forwarded headers are never
/// honored without that allowlist, so <c>X-Forwarded-For</c> cannot be spoofed.
/// </summary>
internal sealed class ForwardedHeadersSettings
{
    /// <summary>Enable processing of forwarded headers. Default false (trust nothing).</summary>
    public bool Enabled { get; set; }

    /// <summary>Trusted proxy IP addresses (e.g. the load balancer). Only these may set forwarded headers.</summary>
    public string[] KnownProxies { get; set; } = [];

    /// <summary>Trusted proxy networks in CIDR form (e.g. <c>10.0.0.0/8</c>).</summary>
    public string[] KnownNetworks { get; set; } = [];

    /// <summary>How many proxy hops to trust when peeling <c>X-Forwarded-For</c>. Default 1.</summary>
    public int ForwardLimit { get; set; } = 1;
}
