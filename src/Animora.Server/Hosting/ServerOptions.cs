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

    /// <summary>File-plane upload concurrency (BUG-FILE-01).</summary>
    public FileSettings Files { get; set; } = new();

    /// <summary>Background-worker throughput tuning (BUG-RECOVERY-01 / BUG-OUTBOX-01).</summary>
    public WorkerSettings Workers { get; set; } = new();
}

/// <summary>
/// BUG-RECOVERY-01 / BUG-OUTBOX-01: throughput knobs of the outbox relay and the intake-recovery worker.
/// Both run on the WORKER admission gate (never the request-path sync gate), so raising these only
/// trades background DB load for drain speed.
/// </summary>
internal sealed class WorkerSettings
{
    /// <summary>Outbox messages claimed per relay tick (1..20, store ceiling). Default 10.</summary>
    public int OutboxClaimBatch { get; set; } = 10;

    /// <summary>Concurrent outbox consumers per tick (1..8). Default 4.</summary>
    public int OutboxConsumers { get; set; } = 4;

    /// <summary>Expired intake leases recovered per tenant per tick (1..50). Default 10.</summary>
    public int RecoveryPerTenantCap { get; set; } = 10;

    /// <summary>Expired intake leases recovered across all tenants per tick (1..2000). Default 200.</summary>
    public int RecoveryTotalCap { get; set; } = 200;

    /// <summary>Tenants recovered in parallel per tick (1..16). Default 4.</summary>
    public int RecoveryParallelism { get; set; } = 4;
}

/// <summary>BUG-FILE-01: bounds on simultaneous chunk uploads (each in-flight chunk holds up to one chunk buffer).</summary>
internal sealed class FileSettings
{
    /// <summary>Concurrent chunk uploads per instance (1..256). Default 16 (≈16 MiB of pooled buffers worst case).</summary>
    public int MaxConcurrentUploads { get; set; } = 16;

    /// <summary>How long an upload waits for a slot before 503 + Retry-After (1..60). Default 5.</summary>
    public int UploadWaitSeconds { get; set; } = 5;
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
