using System.Text;
using System.Text.Json;
using Animora.LiveTest.Capacity;

namespace Animora.LiveTest.Reporting;

/// <summary>Aggregate of one capacity run; renders an English Markdown report and a JSON file with the full hardware time series.</summary>
public sealed class CapacityReport(string runId, string serverUrl, string tenant, DateTimeOffset startedUtc, double p95LimitMs, bool sshEnabled)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public string RunId { get; } = runId;

    public DateTimeOffset FinishedUtc { get; set; }

    public int Devices { get; set; }

    public string? MonitorProblem { get; set; }

    /// <summary>
    /// How realistically the virtual users behaved (think time, ambient polling). Rendered in the report so a reader
    /// can tell a synthetic "no pauses" number from a real-desktop-equivalent one.
    /// </summary>
    public string? Pacing { get; set; }

    public int? PgMaxConnections { get; set; }

    public List<ProfileOutcome> Profiles { get; } = [];

    public IReadOnlyList<ServerSample> ServerSeries { get; set; } = [];

    public IReadOnlyList<HealthSample> HealthSeries { get; set; } = [];

    public int Recommended => CapacityAnalysis.RecommendedMax(Profiles);

    /// <summary>Capacity runs report measurements; they pass unless the very first level of every profile was already broken.</summary>
    public bool Passed => Profiles.Count > 0 && Recommended > 0;

    public string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Animora live capacity report");
        sb.AppendLine();
        sb.AppendLine("> **Planning reference only.** Numbers come from one run against the live server from one client machine;");
        sb.AppendLine("> network distance, client CPU and background load on the server all affect them. Do not treat them as a guarantee.");
        sb.AppendLine();
        sb.AppendLine($"- **Run id:** `{RunId}`  ·  **Server:** `{serverUrl}`  ·  **Tenant:** `{tenant}`");
        sb.AppendLine($"- **Started:** {startedUtc:u}  ·  **Duration:** {(FinishedUtc - startedUtc).TotalMinutes:0.0} min  ·  **Device sessions used:** {Devices}");
        sb.AppendLine($"- **Health rule:** a level is unhealthy at >{CapacityAnalysis.MaxFailureRatio:P0} unexpected failures, p95 > {p95LimitMs:0} ms, a failed `/health/ready` probe, or the service not active.");
        sb.AppendLine($"- **Server hardware monitor:** {(sshEnabled ? (MonitorProblem is null ? "SSH, read-only" : $"SSH requested but degraded — {MonitorProblem}") : "off (client-side metrics + /health/ready only; server-side metrics unavailable)")}");
        sb.AppendLine();
        sb.AppendLine("## Verdict");
        sb.AppendLine();
        var recommended = Recommended;
        sb.AppendLine(recommended > 0
            ? $"- **Recommended maximum concurrent virtual users: {recommended}** (highest level every profile survived)."
            : "- **No level was healthy.** Even the lowest level broke: check connectivity/credentials first, then the hints below.");
        if (recommended > 0 && string.IsNullOrEmpty(Pacing))
            sb.AppendLine($"- Planning estimate: ≈ **{recommended * CapacityAnalysis.ActivityFactor} real clinic desktops** — a virtual user never pauses, while a real desktop talks to the server ~1/{CapacityAnalysis.ActivityFactor} of the time (estimate, not measured).");
        if (!string.IsNullOrEmpty(Pacing))
            sb.AppendLine($"- **Load realism:** {Pacing}");
        foreach (var p in Profiles)
        {
            var broke = p.BrokeAtConcurrency is { } b ? $"broke at {b}" : "did not break";
            sb.AppendLine($"- **{p.Name}:** healthy up to {p.MaxHealthyConcurrency}, {broke} — limiter: **{p.Limiter}**. {p.Hint}");
        }

        foreach (var p in Profiles)
        {
            sb.AppendLine();
            sb.AppendLine($"## Profile `{p.Name}`");
            sb.AppendLine();
            sb.AppendLine(p.Description);
            sb.AppendLine();
            sb.AppendLine("| Users | Requests | Throughput/s | p50 ms | p95 ms | p99 ms | Failures | Busy (503/429) | Host CPU avg/peak | App CPU avg/peak | App RSS | RAM avail min | PG conns peak | Health | Verdict |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (var l in p.Levels)
            {
                var r = l.Result;
                var m = l.Monitor;
                // "Host CPU" is the whole machine (it also contains the load generator when it runs on the server).
                // "App CPU" is the Animora.Server process only — the figure to size a server by.
                var cpu = m.ServerMetricsAvailable ? $"{m.CpuAveragePercent:0}/{m.CpuPeakPercent:0}%" : "n/a";
                var appCpu = m.ServiceCpuAveragePercent is { } a && m.ServiceCpuPeakPercent is { } pk ? $"{a:0}/{pk:0}%" : "n/a";
                var appRss = m.ServiceRssPeakMb is { } rss ? $"{rss:0} MiB" : "n/a";
                var ram = m.ServerMetricsAvailable ? $"{m.MemAvailableMinMb:0} MiB" : "n/a";
                var pg = m.PgConnectionsPeak is { } peak ? (m.PgMaxConnections is { } max ? $"{peak}/{max}" : peak.ToString()) : "n/a";
                var health = m.HealthProbes == 0 ? "n/a" : $"{m.HealthProbes - m.HealthNotReady}/{m.HealthProbes} ready";
                sb.AppendLine($"| {r.Concurrency} | {r.Requests} | {r.Throughput:0.0} | {r.P50:0} | {r.P95:0} | {r.P99:0} | {r.UnexpectedFailures} | {r.ExpectedBusy} | {cpu} | {appCpu} | {appRss} | {ram} | {pg} | {health} | {(l.Healthy ? "OK" : "BROKEN — " + l.UnhealthyReason)} |");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Notes");
        sb.AppendLine();
        sb.AppendLine("- All created rows are prefixed `lt-cap<run>-` and exist only in the dedicated test tenant; the tool does not delete them (most aggregates have no delete verb). Reset the test tenant when convenient.");
        sb.AppendLine("- 503 + Retry-After and 429 answers are the server's documented back-pressure: they are counted as *Busy*, never as failures.");
        sb.AppendLine("- `<run>.json` next to this file carries the full per-second hardware time series for charts.");
        sb.AppendLine("- **Host CPU** is the whole machine; **App CPU** is the `Animora.Server` process alone. Both are % of the machine, so they are directly comparable. If the load generator runs on the same host, Host CPU includes it — size a server by **App CPU** plus PostgreSQL.");
        return sb.ToString();
    }

    public string ToJson() => JsonSerializer.Serialize(new
    {
        runId = RunId, serverUrl, tenant, startedUtc, finishedUtc = FinishedUtc, devices = Devices, p95LimitMs,
        recommendedMaxConcurrentUsers = Recommended, activityFactor = CapacityAnalysis.ActivityFactor,
        pacing = Pacing,
        sshEnabled, monitorProblem = MonitorProblem, pgMaxConnections = PgMaxConnections,
        profiles = Profiles, serverSeries = ServerSeries, healthSeries = HealthSeries,
        disclaimer = "Planning reference only (A-RULE 035).",
    }, JsonOptions);

    /// <summary>Writes <c>capacity-&lt;run&gt;.md</c> and <c>.json</c> under <paramref name="outputDir"/>; returns the markdown path.</summary>
    public string Write(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        var md = Path.Combine(outputDir, $"capacity-{RunId}.md");
        File.WriteAllText(md, ToMarkdown());
        File.WriteAllText(Path.Combine(outputDir, $"capacity-{RunId}.json"), ToJson());
        return md;
    }
}
