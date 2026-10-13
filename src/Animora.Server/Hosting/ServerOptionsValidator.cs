using Microsoft.Extensions.Options;

namespace Animora.Server.Hosting;

/// <summary>
/// Validates <see cref="ServerOptions"/> at boot (<c>ValidateOnStart</c>). A
/// failure stops the process with a message that names the offending key — a
/// misconfiguration must fail closed at startup (ARCHITECTURE.md §4).
/// </summary>
internal sealed class ServerOptionsValidator : IValidateOptions<ServerOptions>
{
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

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
