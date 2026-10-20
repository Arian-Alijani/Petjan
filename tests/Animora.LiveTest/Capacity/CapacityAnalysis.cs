using System.Globalization;
using System.Text.Json.Serialization;

namespace Animora.LiveTest.Capacity;

/// <summary>One measured level of one profile together with the server hardware window observed while it ran.</summary>
public sealed record LevelResult(
    [property: JsonPropertyName("result")] ProfileResult Result,
    [property: JsonPropertyName("monitor")] MonitorSummary Monitor,
    [property: JsonPropertyName("healthy")] bool Healthy,
    [property: JsonPropertyName("reason")] string? UnhealthyReason);

/// <summary>Outcome of ramping one profile: every level run (the ramp stops at the first unhealthy level) and the verdict.</summary>
public sealed record ProfileOutcome(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("levels")] IReadOnlyList<LevelResult> Levels,
    [property: JsonPropertyName("maxHealthyConcurrency")] int MaxHealthyConcurrency,
    [property: JsonPropertyName("brokeAtConcurrency")] int? BrokeAtConcurrency,
    [property: JsonPropertyName("limiter")] string Limiter,
    [property: JsonPropertyName("hint")] string Hint);

/// <summary>Pure decision logic: is a level healthy, and which resource limits the server. No I/O, deterministic.</summary>
public static class CapacityAnalysis
{
    /// <summary>Failure ratio above which a level counts as broken (1 %).</summary>
    public const double MaxFailureRatio = 0.01;

    /// <summary>CPU peak (%) at/above which the server counts as CPU-saturated.</summary>
    public const double CpuSaturationPercent = 85;

    /// <summary>Available-memory fraction at/below which the server counts as memory-pressured.</summary>
    public const double MemoryPressureFraction = 0.10;

    /// <summary>PostgreSQL connections / max_connections at/above which the DB counts as connection-bound.</summary>
    public const double PgConnectionFraction = 0.80;

    /// <summary>
    /// Rule of thumb for the planning estimate: a real desktop is actively talking to the server roughly 10 % of the time,
    /// whereas a load-test virtual user never pauses. Labelled as an estimate in every report.
    /// </summary>
    public const int ActivityFactor = 10;

    /// <summary>Judges one level: failures, latency, server readiness and service liveness all count.</summary>
    public static (bool Healthy, string? Reason) Judge(ProfileResult r, MonitorSummary m, double p95LimitMs)
    {
        var reasons = new List<string>();
        var failureRatio = r.Requests == 0 ? 1.0 : (double)r.UnexpectedFailures / r.Requests;
        if (failureRatio > MaxFailureRatio)
            reasons.Add($"{r.UnexpectedFailures}/{r.Requests} unexpected failures ({failureRatio:P1} > {MaxFailureRatio:P0})");
        if (r.P95 > p95LimitMs)
            reasons.Add($"p95 {r.P95:0} ms > limit {p95LimitMs:0} ms");
        if (m.HealthNotReady > 0)
            reasons.Add($"/health/ready not ready in {m.HealthNotReady}/{m.HealthProbes} probes");
        if (m.ServiceDownSamples > 0)
            reasons.Add($"service not active in {m.ServiceDownSamples} sample(s)");
        return reasons.Count == 0 ? (true, null) : (false, string.Join("; ", reasons));
    }

    /// <summary>
    /// Names the saturated resource at the breaking level (or at the highest level when nothing broke) and gives an
    /// actionable "upgrade X" hint. When server metrics are missing the hint says so instead of guessing.
    /// </summary>
    public static (string Limiter, string Hint) Diagnose(MonitorSummary m, bool broke)
    {
        if (!m.ServerMetricsAvailable)
        {
            return ("unknown", broke
                ? "Latency/failures broke but server hardware was not observed. Re-run with the SSH monitor (ANIMORA_LT_SSH=1) to learn which resource to upgrade."
                : "No break point reached and server hardware was not observed. Raise ANIMORA_LT_LEVELS and enable the SSH monitor for a hardware verdict.");
        }

        var causes = new List<(string Limiter, string Hint)>();
        // Prefer the server process' own CPU when sampled: the host figure also contains the load generator.
        var appCpu = m.ServiceCpuPeakPercent ?? m.CpuPeakPercent;
        var cpuLabel = m.ServiceCpuPeakPercent is null ? "peak" : "app peak";
        if (appCpu >= CpuSaturationPercent)
            causes.Add(("cpu", $"CPU-bound: {cpuLabel} {appCpu:0}% (host peak {m.CpuPeakPercent:0}%, load1 {m.Load1Peak:0.0}). Add vCPUs / a faster CPU before anything else."));
        var memFraction = m.MemTotalMb <= 0 ? 1.0 : m.MemAvailableMinMb / m.MemTotalMb;
        if (memFraction <= MemoryPressureFraction || m.SwapUsedPeakMb > 64)
            causes.Add(("memory", $"RAM-bound: only {m.MemAvailableMinMb:0} MiB of {m.MemTotalMb:0} MiB left at the worst moment (swap peak {m.SwapUsedPeakMb:0} MiB). Add RAM."));
        if (m.PgConnectionsPeak is { } peak && m.PgMaxConnections is { } max && max > 0 && (double)peak / max >= PgConnectionFraction)
            causes.Add(("postgres-connections", $"PostgreSQL connection-bound: {peak}/{max} connections at peak. Raise max_connections (and RAM) or revisit the pool sizes."));

        if (causes.Count > 0)
        {
            var limiter = string.Join("+", causes.Select(c => c.Limiter));
            return (limiter, string.Join(" ", causes.Select(c => c.Hint)));
        }

        return broke
            ? ("software-or-disk", "Latency/failures broke WITHOUT CPU, RAM or PostgreSQL-connection saturation: look at lock contention, disk I/O, the server's own rate limits / admission control, or the network path before buying hardware.")
            : ("none-reached", $"No break point reached: CPU peak {m.CpuPeakPercent:0}%{(m.ServiceCpuPeakPercent is { } a ? $", app CPU peak {a:0}%" : string.Empty)}, RAM available min {m.MemAvailableMinMb:0} MiB. Raise ANIMORA_LT_LEVELS to find the limit.");
    }

    /// <summary>Folds the measured levels of one profile into a verdict (levels must be in ascending concurrency).</summary>
    public static ProfileOutcome Conclude(string name, string description, IReadOnlyList<LevelResult> levels)
    {
        var maxHealthy = levels.Where(l => l.Healthy).Select(l => l.Result.Concurrency).DefaultIfEmpty(0).Max();
        var broke = levels.FirstOrDefault(l => !l.Healthy);
        var focus = broke ?? levels.LastOrDefault();
        var (limiter, hint) = focus is null
            ? ("unknown", "No level was measured.")
            : Diagnose(focus.Monitor, broke is not null);
        return new ProfileOutcome(name, description, levels, maxHealthy, broke?.Result.Concurrency, limiter, hint);
    }

    /// <summary>Recommended concurrent virtual users = the lowest healthy ceiling across profiles (the weakest workload decides).</summary>
    public static int RecommendedMax(IReadOnlyList<ProfileOutcome> outcomes) =>
        outcomes.Count == 0 ? 0 : outcomes.Min(o => o.MaxHealthyConcurrency);

    public static string Percent(double v) => v.ToString("0", CultureInfo.InvariantCulture) + "%";
}
