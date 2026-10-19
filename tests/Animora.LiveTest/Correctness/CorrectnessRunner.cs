using System.Diagnostics;
using Animora.LiveTest.Client;
using Animora.LiveTest.Config;
using Animora.LiveTest.Reporting;

namespace Animora.LiveTest.Correctness;

/// <summary>
/// Runs every <see cref="ScenarioDef"/> against the live server with two logged-in virtual desktops, collects a
/// <see cref="CorrectnessReport"/> and cleans up what it can. Stops at the first unexpected failure unless
/// <see cref="LiveTestOptions.StopOnFirstFailure"/> is off (expected Conflict / Rejected / Deferred answers are assertions, not failures).
/// </summary>
public static class CorrectnessRunner
{
    public static async Task<int> RunAsync(LiveTestOptions options, CancellationToken ct = default)
    {
        var problems = options.Validate(requiresAuth: true);
        if (problems.Count > 0)
        {
            foreach (var p in problems) Console.Error.WriteLine($"  config: {p}");
            return 78;
        }

        var scenarios = ScenarioCatalog.All
            .Where(s => options.ScenarioFilter.Length == 0 ||
                        $"{s.Feature}/{s.Name}".Contains(options.ScenarioFilter, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (scenarios.Count == 0)
        {
            Console.Error.WriteLine($"No scenario matches filter '{options.ScenarioFilter}'.");
            return 64;
        }

        var runId = Convert.ToHexStringLower(Guid.NewGuid().ToByteArray()).Substring(0, 6);
        var report = new CorrectnessReport(runId, options.ServerBaseUrl, options.TenantCode, DateTimeOffset.UtcNow);
        Console.WriteLine($"Correctness run {runId}: {scenarios.Count} scenario(s), tenant '{options.TenantCode}'. Data prefix: lt-{runId}-");

        await using var a = VirtualClient.Create(options, "A", slot: 0);
        await using var b = VirtualClient.Create(options, "B", slot: 1);
        foreach (var client in new[] { a, b })
        {
            var error = await client.LoginAsync(ct).ConfigureAwait(false);
            if (error is not null)
            {
                Console.Error.WriteLine($"  client {client.Label}: {error}");
                return 1;
            }

            Console.WriteLine($"  client {client.Label}: logged in as role '{client.Role}' device {client.DeviceId}");
        }

        var context = new ScenarioContext(options, runId, a, b, ct);
        var stopped = false;
        foreach (var scenario in scenarios)
        {
            if (stopped)
            {
                report.Results.Add(new ScenarioResult(scenario.Feature, scenario.Name, ScenarioStatus.NotRun, TimeSpan.Zero,
                    "not run: an earlier scenario failed (set ANIMORA_LT_KEEP_GOING=1 to continue past failures).", []));
                continue;
            }

            Console.WriteLine($"  ▶ [{scenario.Feature}] {scenario.Name}");
            context.BeginScenario();
            var sw = Stopwatch.StartNew();
            ScenarioStatus status;
            string? detail = null;
            try
            {
                await scenario.Run(context).ConfigureAwait(false);
                status = ScenarioStatus.Pass;
            }
            catch (ScenarioSkipped skip)
            {
                status = ScenarioStatus.Skip;
                detail = skip.Message;
            }
            catch (ScenarioFailure failure)
            {
                status = ScenarioStatus.Fail;
                detail = failure.Message;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                status = ScenarioStatus.Error;
                detail = "cancelled by the operator.";
                stopped = true;
            }
            catch (Exception ex)
            {
                status = ScenarioStatus.Error;
                detail = $"{ex.GetType().Name}: {ex.Message}";
            }

            sw.Stop();
            Console.WriteLine($"    {status.ToString().ToUpperInvariant()} ({sw.Elapsed.TotalSeconds:0.0}s){(detail is null ? string.Empty : $" — {detail}")}");
            report.Results.Add(new ScenarioResult(scenario.Feature, scenario.Name, status, sw.Elapsed, detail, context.Steps.ToArray()));
            if (status is ScenarioStatus.Fail or ScenarioStatus.Error && options.StopOnFirstFailure) stopped = true;
        }

        var (done, skippedCleanup) = await context.RunCleanupAsync().ConfigureAwait(false);
        report.CleanupDone = done;
        report.CleanupSkipped = skippedCleanup;
        report.FinishedUtc = DateTimeOffset.UtcNow;

        var path = report.Write(options.OutputDir);
        Console.WriteLine();
        Console.WriteLine($"Verdict: {(report.Passed ? "PASS" : "FAIL")} — {report.Count(ScenarioStatus.Pass)} pass, {report.Count(ScenarioStatus.Fail)} fail, " +
                          $"{report.Count(ScenarioStatus.Error)} error, {report.Count(ScenarioStatus.Skip)} skip, {report.Count(ScenarioStatus.NotRun)} not run.");
        Console.WriteLine($"Report: {path}");
        return report.Passed ? 0 : 1;
    }
}
