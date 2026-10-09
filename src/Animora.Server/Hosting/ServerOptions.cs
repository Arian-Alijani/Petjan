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
}
