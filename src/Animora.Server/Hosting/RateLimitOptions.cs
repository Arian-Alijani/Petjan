namespace Animora.Server.Hosting;

/// <summary>
/// Per-policy fixed-window settings, bound from
/// <c>Server:RateLimits:&lt;policy&gt;</c>. Both values must be &gt; 0 (validated by
/// <see cref="ServerOptionsValidator"/>). Defaults match ARCHITECTURE.md §4.
/// </summary>
internal sealed class RateLimitWindowOptions
{
    /// <summary>Permitted requests per window. Must be &gt; 0.</summary>
    public int PermitLimit { get; set; }

    /// <summary>Window length in seconds. Must be &gt; 0.</summary>
    public int WindowSeconds { get; set; }
}

/// <summary>
/// The five named rate-limit policies (ARCHITECTURE.md §4). Bound from
/// <c>Server:RateLimits</c>; a missing policy falls back to the baked default.
/// </summary>
internal sealed class RateLimitOptions
{
    public RateLimitWindowOptions Auth { get; set; } = new() { PermitLimit = 10, WindowSeconds = 60 };
    public RateLimitWindowOptions Commands { get; set; } = new() { PermitLimit = 120, WindowSeconds = 60 };
    public RateLimitWindowOptions Sync { get; set; } = new() { PermitLimit = 60, WindowSeconds = 60 };
    public RateLimitWindowOptions Files { get; set; } = new() { PermitLimit = 60, WindowSeconds = 60 };
    public RateLimitWindowOptions Operator { get; set; } = new() { PermitLimit = 30, WindowSeconds = 60 };

    /// <summary>Resolves a policy's window by its <see cref="RateLimitPolicies"/> key.</summary>
    public RateLimitWindowOptions ForPolicy(string policy) => policy switch
    {
        RateLimitPolicies.Auth => Auth,
        RateLimitPolicies.Commands => Commands,
        RateLimitPolicies.Sync => Sync,
        RateLimitPolicies.Files => Files,
        RateLimitPolicies.Operator => Operator,
        _ => throw new ArgumentOutOfRangeException(nameof(policy), policy, "Unknown rate-limit policy."),
    };
}
