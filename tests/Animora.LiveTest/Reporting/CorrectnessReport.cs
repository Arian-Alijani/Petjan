using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Animora.LiveTest.Reporting;

/// <summary>Verdict of one correctness scenario.</summary>
public enum ScenarioStatus
{
    Pass,
    Fail,
    Error,
    Skip,
    NotRun,
}

/// <summary>Result of one scenario with enough detail to debug a desync without re-running.</summary>
public sealed record ScenarioResult(string Feature, string Name, ScenarioStatus Status, TimeSpan Elapsed, string? Detail, IReadOnlyList<string> Steps);

/// <summary>Aggregate of one correctness run; renders English Markdown and JSON.</summary>
public sealed class CorrectnessReport(string runId, string serverUrl, string tenant, DateTimeOffset startedUtc)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string RunId { get; } = runId;

    public string ServerUrl { get; } = serverUrl;

    public string Tenant { get; } = tenant;

    public DateTimeOffset StartedUtc { get; } = startedUtc;

    public DateTimeOffset FinishedUtc { get; set; }

    public int CleanupDone { get; set; }

    public int CleanupSkipped { get; set; }

    public List<ScenarioResult> Results { get; } = [];

    public int Count(ScenarioStatus s) => Results.Count(r => r.Status == s);

    /// <summary>True when nothing failed or errored (skips and not-run scenarios do not count as failure).</summary>
    public bool Passed => Count(ScenarioStatus.Fail) == 0 && Count(ScenarioStatus.Error) == 0;

    public string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Animora live correctness report");
        sb.AppendLine();
        sb.AppendLine($"- **Run id:** `{RunId}`");
        sb.AppendLine($"- **Server:** `{ServerUrl}`  ·  **Tenant:** `{Tenant}`");
        sb.AppendLine($"- **Started:** {StartedUtc:u}  ·  **Duration:** {(FinishedUtc - StartedUtc).TotalSeconds:0.0}s");
        sb.AppendLine($"- **Verdict:** {(Passed ? "PASS" : "FAIL")}  —  " +
                      $"{Count(ScenarioStatus.Pass)} pass · {Count(ScenarioStatus.Fail)} fail · {Count(ScenarioStatus.Error)} error · " +
                      $"{Count(ScenarioStatus.Skip)} skip · {Count(ScenarioStatus.NotRun)} not run");
        sb.AppendLine($"- **Cleanup:** {CleanupDone} teardown action(s) ran, {CleanupSkipped} skipped. All created data is prefixed `lt-{RunId}-`.");
        sb.AppendLine();
        sb.AppendLine("| # | Feature | Scenario | Result | Time |");
        sb.AppendLine("|---|---|---|---|---|");
        for (var i = 0; i < Results.Count; i++)
        {
            var r = Results[i];
            sb.AppendLine($"| {i + 1} | {r.Feature} | {Escape(r.Name)} | {Label(r.Status)} | {r.Elapsed.TotalSeconds:0.0}s |");
        }

        var problems = Results.Where(r => r.Status is ScenarioStatus.Fail or ScenarioStatus.Error).ToList();
        if (problems.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Failures");
            foreach (var r in problems)
            {
                sb.AppendLine();
                sb.AppendLine($"### {r.Feature} — {r.Name}  ({Label(r.Status)})");
                sb.AppendLine();
                sb.AppendLine("```text");
                sb.AppendLine(r.Detail);
                sb.AppendLine("```");
                if (r.Steps.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("Steps before the failure:");
                    foreach (var step in r.Steps) sb.AppendLine($"1. {step}");
                }
            }
        }

        var skipped = Results.Where(r => r.Status == ScenarioStatus.Skip).ToList();
        if (skipped.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Skipped (tenant precondition missing — NOT a pass)");
            foreach (var r in skipped) sb.AppendLine($"- **{r.Feature} — {r.Name}:** {r.Detail}");
        }

        return sb.ToString();
    }

    public string ToJson() => JsonSerializer.Serialize(new
    {
        runId = RunId, serverUrl = ServerUrl, tenant = Tenant, startedUtc = StartedUtc, finishedUtc = FinishedUtc, passed = Passed,
        totals = new { pass = Count(ScenarioStatus.Pass), fail = Count(ScenarioStatus.Fail), error = Count(ScenarioStatus.Error), skip = Count(ScenarioStatus.Skip), notRun = Count(ScenarioStatus.NotRun) },
        cleanup = new { done = CleanupDone, skipped = CleanupSkipped },
        results = Results.Select(r => new { r.Feature, r.Name, r.Status, seconds = Math.Round(r.Elapsed.TotalSeconds, 2), r.Detail, r.Steps }),
    }, JsonOptions);

    /// <summary>Writes <c>correctness-&lt;run&gt;.md</c> and <c>.json</c> under <paramref name="outputDir"/>; returns the markdown path.</summary>
    public string Write(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        var md = Path.Combine(outputDir, $"correctness-{RunId}.md");
        File.WriteAllText(md, ToMarkdown());
        File.WriteAllText(Path.Combine(outputDir, $"correctness-{RunId}.json"), ToJson());
        return md;
    }

    private static string Label(ScenarioStatus s) => s switch
    {
        ScenarioStatus.Pass => "PASS",
        ScenarioStatus.Fail => "FAIL",
        ScenarioStatus.Error => "ERROR",
        ScenarioStatus.Skip => "SKIP",
        _ => "NOT RUN",
    };

    private static string Escape(string s) => s.Replace("|", "\\|", StringComparison.Ordinal);
}
