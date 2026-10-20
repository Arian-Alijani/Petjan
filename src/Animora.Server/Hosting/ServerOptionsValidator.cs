using Microsoft.Extensions.Options;

namespace Animora.Server.Hosting;

/// <summary>
/// Validates <see cref="ServerOptions"/> at boot (<c>ValidateOnStart</c>). A
/// failure stops the process with a message that names the offending key — a
/// misconfiguration must fail closed at startup (ARCHITECTURE.md §4).
/// </summary>
internal sealed class ServerOptionsValidator : IValidateOptions<ServerOptions>
{
    private static readonly int OutboundTimeoutSeconds = (int)Animora.Server.Resilience.OutboundTimeout.Default.TotalSeconds;

    public ValidateOptionsResult Validate(string? name, ServerOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.DataRootPath))
        {
            failures.Add($"'{ServerOptions.SectionName}:{nameof(ServerOptions.DataRootPath)}' is required (set it via the Server__DataRootPath environment variable).");
        }
        else if (!Path.IsPathRooted(options.DataRootPath))
        {
            failures.Add($"'{ServerOptions.SectionName}:{nameof(ServerOptions.DataRootPath)}' must be an absolute path.");
        }

        if (!string.IsNullOrWhiteSpace(options.PublicBaseUrl) &&
            !Uri.TryCreate(options.PublicBaseUrl, UriKind.Absolute, out _))
        {
            failures.Add($"'{ServerOptions.SectionName}:{nameof(ServerOptions.PublicBaseUrl)}' must be an absolute URI when set.");
        }

        foreach (var policy in RateLimitPolicies.All)
        {
            var window = options.RateLimits.ForPolicy(policy);
            if (window.PermitLimit <= 0)
            {
                failures.Add($"'{ServerOptions.SectionName}:RateLimits:{policy}:PermitLimit' must be greater than 0.");
            }

            if (window.WindowSeconds <= 0)
            {
                failures.Add($"'{ServerOptions.SectionName}:RateLimits:{policy}:WindowSeconds' must be greater than 0.");
            }
        }

        // SEC-FWD-01: if forwarded headers are enabled, a trusted-proxy allowlist is mandatory —
        // enabling them with an empty allowlist would make X-Forwarded-For attacker-controllable.
        var fwd = options.ForwardedHeaders;
        if (fwd.Enabled && fwd.KnownProxies.Length == 0 && fwd.KnownNetworks.Length == 0)
        {
            failures.Add($"'{ServerOptions.SectionName}:{nameof(ServerOptions.ForwardedHeaders)}' is enabled but no KnownProxies/KnownNetworks allowlist is configured; forwarded headers must never be trusted without an allowlist.");
        }

        if (fwd.Enabled && fwd.ForwardLimit <= 0)
        {
            failures.Add($"'{ServerOptions.SectionName}:{nameof(ServerOptions.ForwardedHeaders)}:{nameof(ForwardedHeadersSettings.ForwardLimit)}' must be greater than 0.");
        }

        if (options.QueryBudget.ReportingMax < 0)
        {
            failures.Add($"'{ServerOptions.SectionName}:QueryBudget:ReportingMax' must be 0 (auto) or greater.");
        }

        if (options.QueryBudget.WaitSeconds is < 1 or > 120)
        {
            failures.Add($"'{ServerOptions.SectionName}:QueryBudget:WaitSeconds' must be between 1 and 120.");
        }

        if (options.Authz.OperationTimeoutSeconds is < 1 or > 30)
        {
            failures.Add($"'{ServerOptions.SectionName}:Authz:OperationTimeoutSeconds' must be between 1 and 30.");
        }

        var cache = options.AuthCache;
        if (cache.TtlSeconds is < 0 or > 60)
        {
            failures.Add($"'{ServerOptions.SectionName}:AuthCache:TtlSeconds' must be between 0 and 60.");
        }

        if (cache.MaxEntries < 1)
        {
            failures.Add($"'{ServerOptions.SectionName}:AuthCache:MaxEntries' must be at least 1.");
        }

        if (options.Files.MaxConcurrentUploads is < 1 or > 256)
        {
            failures.Add($"'{ServerOptions.SectionName}:Files:MaxConcurrentUploads' must be between 1 and 256.");
        }

        if (options.Files.UploadWaitSeconds is < 1 or > 60)
        {
            failures.Add($"'{ServerOptions.SectionName}:Files:UploadWaitSeconds' must be between 1 and 60.");
        }

        // SEC-HSTS-01: a TLS-terminating proxy is only safe/functional with trusted forwarded-proto (else redirect loop).
        if (options.Tls.Termination == TlsTermination.Proxy && !fwd.Enabled)
        {
            failures.Add($"'{ServerOptions.SectionName}:Tls:Termination' = Proxy requires '{ServerOptions.SectionName}:ForwardedHeaders:Enabled' = true with a KnownProxies/KnownNetworks allowlist (otherwise the HTTPS redirect loops behind the proxy).");
        }

        if (options.Tls.PublicHttpsPort is < 1 or > 65535)
        {
            failures.Add($"'{ServerOptions.SectionName}:Tls:PublicHttpsPort' must be between 1 and 65535.");
        }

        if (options.Health.ReadinessCacheSeconds is < 0 or > 60)
            failures.Add($"'{ServerOptions.SectionName}:Health:ReadinessCacheSeconds' must be between 0 and 60.");

        var w = options.Workers;
        if (w.OutboxClaimBatch is < 1 or > 20)
            failures.Add($"'{ServerOptions.SectionName}:Workers:OutboxClaimBatch' must be between 1 and 20.");
        // PERF-MC-01: 0 = auto (scale with cores). Reject only out-of-range EXPLICIT values.
        if (w.OutboxConsumers is < 0 or > 8)
            failures.Add($"'{ServerOptions.SectionName}:Workers:OutboxConsumers' must be 0 (auto) or between 1 and 8.");
        // Each delivery is bounded by OutboundTimeout.Default (10 s); every consumer drains
        // ceil(batch / consumers) messages sequentially, which must stay inside the 60 s claim lease.
        // The lease is hardest with the FEWEST consumers, so validate with the guaranteed-minimum
        // effective consumer count: an explicit value if set, else the auto floor (2). Auto only ever
        // widens fan-out with cores, which can only make this constraint easier, never harder.
        else
        {
            int minConsumers = w.OutboxConsumers > 0 ? w.OutboxConsumers : 2;
            if (w.OutboxClaimBatch is >= 1 and <= 20 &&
                (int)Math.Ceiling(w.OutboxClaimBatch / (double)minConsumers) * OutboundTimeoutSeconds > 50)
                failures.Add($"'{ServerOptions.SectionName}:Workers:OutboxClaimBatch' is too large for OutboxConsumers: worst-case delivery time would exceed the outbox claim lease.");
        }
        if (w.RecoveryPerTenantCap is < 1 or > 50)
            failures.Add($"'{ServerOptions.SectionName}:Workers:RecoveryPerTenantCap' must be between 1 and 50.");
        if (w.RecoveryTotalCap is < 1 or > 2000)
            failures.Add($"'{ServerOptions.SectionName}:Workers:RecoveryTotalCap' must be between 1 and 2000.");
        // PERF-MC-01: 0 = auto (scale with cores). Reject only out-of-range explicit values.
        if (w.RecoveryParallelism is < 0 or > 16)
            failures.Add($"'{ServerOptions.SectionName}:Workers:RecoveryParallelism' must be 0 (auto) or between 1 and 16.");

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
