namespace Animora.Server.Hosting;

/// <summary>
/// Named rate-limit policy keys (ARCHITECTURE.md §4). Each maps to a fixed-window
/// limiter configured in <c>Program.cs</c> from <see cref="RateLimitOptions"/>.
/// Health endpoints are never rate-limited.
/// </summary>
internal static class RateLimitPolicies
{
    /// <summary>Sign-in / refresh surface (per IP). Default 10/min.</summary>
    public const string Auth = "auth";

    /// <summary>Command intake (per tenant-or-IP). Default 120/min.</summary>
    public const string Commands = "commands";

    /// <summary>State-sync pull surface. Default 60/min.</summary>
    public const string Sync = "sync";

    /// <summary>File valet-key surface. Default 60/min.</summary>
    public const string Files = "files";

    /// <summary>Operator API. Default 30/min.</summary>
    public const string Operator = "operator";

    /// <summary>All five policy names, in a stable order (used by tests / setup).</summary>
    public static readonly IReadOnlyList<string> All = [Auth, Commands, Sync, Files, Operator];
}
