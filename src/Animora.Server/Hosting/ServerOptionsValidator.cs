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

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
