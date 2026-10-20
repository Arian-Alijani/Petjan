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

    /// <summary>
    /// Expose first-party OpenAPI even outside Development. Default <b>false</b> (SEC-OPENAPI-01). The schema lists
    /// every route, parameter and error shape and is served ANONYMOUSLY by <c>MapOpenApi</c>, so enabling it in
    /// production is an information-disclosure decision: only do so on a non-public listener / management network
    /// (reverse-proxy ACL). The host logs a warning at startup whenever it is exposed outside Development.
    /// </summary>
    public bool EnableOpenApi { get; set; }

    /// <summary>Who terminates TLS and how the app enforces HTTPS (SEC-HSTS-01).</summary>
    public TlsSettings Tls { get; set; } = new();

    /// <summary>Optional public base URL (absolute) used to build external links.</summary>
    public string? PublicBaseUrl { get; set; }

    /// <summary>The five named rate-limit windows (ARCHITECTURE.md §4).</summary>
    public RateLimitOptions RateLimits { get; set; } = new();

    /// <summary>Trusted-proxy / forwarded-headers configuration (SEC-FWD-01).</summary>
    public ForwardedHeadersSettings ForwardedHeaders { get; set; } = new();

    /// <summary>Reporting query admission (PERF-QB-01).</summary>
    public QueryBudgetSettings QueryBudget { get; set; } = new();

    /// <summary>Per-operation bounds of the authorization pipeline's database calls (IMP-06).</summary>
    public AuthzSettings Authz { get; set; } = new();

    /// <summary>Short-TTL authorization/entitlement verdict cache (PERF-AUTHZ-01).</summary>
    public AuthCacheSettings AuthCache { get; set; } = new();

    /// <summary>File-plane upload concurrency (BUG-FILE-01).</summary>
    public FileSettings Files { get; set; } = new();

    /// <summary>Background-worker throughput tuning (BUG-RECOVERY-01 / BUG-OUTBOX-01).</summary>
    public WorkerSettings Workers { get; set; } = new();

    /// <summary>Health-probe caching (BUG-HEALTH-01).</summary>
    public HealthSettings Health { get; set; } = new();
}

/// <summary>Where TLS terminates (SEC-HSTS-01).</summary>
internal enum TlsTermination
{
    /// <summary>Kestrel terminates TLS (or the app is reached over HTTPS directly). Redirect/HSTS use the real scheme.</summary>
    App,

    /// <summary>
    /// A reverse proxy / load balancer terminates TLS and talks plain HTTP to the app. REQUIRES
    /// <c>Server:ForwardedHeaders</c> (allowlisted proxy) so <c>X-Forwarded-Proto</c> is the source of the scheme;
    /// without it every request looks like HTTP and the HTTPS redirect would loop. Enforced at boot by the validator.
    /// </summary>
    Proxy,
}

/// <summary>
/// SEC-HSTS-01: transport-security contract. Outside Development the main API relies on
/// <c>UseHttpsRedirection</c> + <c>UseHsts</c>; the admin surface additionally answers 400 on non-HTTPS
/// (<c>AdminSecurityHeadersMiddleware</c>). Both read <c>Request.IsHttps</c>, which behind a proxy is correct only
/// when forwarded-proto processing is active - hence <see cref="TlsTermination.Proxy"/> demands it.
/// </summary>
internal sealed class TlsSettings
{
    /// <summary>Default <see cref="TlsTermination.App"/>.</summary>
    public TlsTermination Termination { get; set; } = TlsTermination.App;

    /// <summary>Public HTTPS port used in redirect targets when the proxy terminates TLS (1..65535). Default 443.</summary>
    public int PublicHttpsPort { get; set; } = 443;
}

/// <summary>BUG-HEALTH-01: how long a <c>/health/ready</c> evaluation is reused by subsequent pollers.</summary>
internal sealed class HealthSettings
{
    /// <summary>Seconds a readiness report stays valid (0..60; 0 = evaluate on every call). Default 5.</summary>
    public int ReadinessCacheSeconds { get; set; } = 5;
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

    /// <summary>
    /// PERF-MC-01: concurrent outbox consumers per tick. <b>0 = auto</b> = scale with CPU cores
    /// (<see cref="CpuTopology.LogicalCores"/>), clamped to [2..8] AND to the worker DB-connection budget
    /// (<c>Database:WorkerMax</c>) so fan-out never exceeds what the worker pool can serve. An explicit
    /// value in 1..8 always wins. Default 0 (auto). See <see cref="EffectiveOutboxConsumers"/>.
    /// </summary>
    public int OutboxConsumers { get; set; }

    /// <summary>Expired intake leases recovered per tenant per tick (1..50). Default 10.</summary>
    public int RecoveryPerTenantCap { get; set; } = 10;

    /// <summary>Expired intake leases recovered across all tenants per tick (1..2000). Default 200.</summary>
    public int RecoveryTotalCap { get; set; } = 200;

    /// <summary>
    /// PERF-MC-01: tenants recovered in parallel per tick. <b>0 = auto</b> = scale with CPU cores
    /// (<see cref="CpuTopology.LogicalCores"/>), clamped to [2..16] AND to the worker DB-connection budget
    /// (<c>Database:WorkerMax</c>). An explicit value in 1..16 always wins. Default 0 (auto).
    /// See <see cref="EffectiveRecoveryParallelism"/>.
    /// </summary>
    public int RecoveryParallelism { get; set; }

    /// <summary>
    /// PERF-MC-01: effective outbox-consumer width. Explicit <see cref="OutboxConsumers"/> (&gt;0) wins;
    /// otherwise auto = <c>ScaleByCores(1, 2, 8)</c>. The result is further clamped to
    /// <paramref name="workerDbBudget"/> (but never below 1) so background fan-out stays within the
    /// PostgreSQL worker-pool budget regardless of core count.
    /// </summary>
    public int EffectiveOutboxConsumers(int workerDbBudget)
    {
        int width = OutboxConsumers > 0 ? OutboxConsumers : CpuTopology.ScaleByCores(1, 2, 8);
        return Math.Max(1, Math.Min(width, Math.Max(1, workerDbBudget)));
    }

    /// <summary>
    /// PERF-MC-01: effective recovery parallelism. Explicit <see cref="RecoveryParallelism"/> (&gt;0) wins;
    /// otherwise auto = <c>ScaleByCores(1, 2, 16)</c>. Clamped to <paramref name="workerDbBudget"/>
    /// (never below 1) so tenant recovery never over-subscribes the worker DB pool.
    /// </summary>
    public int EffectiveRecoveryParallelism(int workerDbBudget)
    {
        int width = RecoveryParallelism > 0 ? RecoveryParallelism : CpuTopology.ScaleByCores(1, 2, 16);
        return Math.Max(1, Math.Min(width, Math.Max(1, workerDbBudget)));
    }
}

/// <summary>IMP-06: how long ONE authorization-pipeline store call may run before the request is shed with 503.</summary>
internal sealed class AuthzSettings
{
    /// <summary>Per-operation timeout of membership/entitlement store lookups, 1..30 s. Default 5 (was: the full 30 s command timeout).</summary>
    public int OperationTimeoutSeconds { get; set; } = 5;
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
