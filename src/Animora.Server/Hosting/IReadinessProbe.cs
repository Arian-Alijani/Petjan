namespace Animora.Server.Hosting;

/// <summary>
/// Outcome of a single readiness probe. Carries only a health flag — never
/// exception text — so <c>/health/ready</c> can report failed probe names
/// without leaking internals (ARCHITECTURE.md §4).
/// </summary>
internal sealed record ReadinessResult(bool IsHealthy)
{
    public static ReadinessResult Healthy() => new(true);

    public static ReadinessResult Unhealthy() => new(false);
}

/// <summary>
/// A named, dependency-level readiness check run by <c>/health/ready</c>. S01
/// ships <see cref="DataRootWritableProbe"/>; S02 adds a database probe and a
/// key-ring probe.
/// </summary>
internal interface IReadinessProbe
{
    /// <summary>Stable probe name surfaced in the <c>failed</c> list (names only).</summary>
    string Name { get; }

    /// <summary>Non-fatal worker probes report degradation without rejecting readiness.</summary>
    bool IsRequired => true;

    /// <summary>Runs the check. Must never throw for an expected failure.</summary>
    Task<ReadinessResult> CheckAsync(CancellationToken ct);
}
