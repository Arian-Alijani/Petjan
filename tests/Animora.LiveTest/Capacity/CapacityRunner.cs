using Animora.LiveTest.Client;
using Animora.LiveTest.Config;
using Animora.LiveTest.Reporting;

namespace Animora.LiveTest.Capacity;

/// <summary>
/// Ramps each load profile through the configured concurrency levels against the live server while the read-only
/// <see cref="RemoteMonitor"/> and the <see cref="HealthProbe"/> record the server's response, then writes the report.
/// Safety: refuses a tenant that does not look like a test tenant, ramps (never jumps), stops a profile at its first
/// unhealthy level, cools down between levels, and treats server back-pressure as expected.
/// </summary>
public static class CapacityRunner
{
    public static async Task<int> RunAsync(LiveTestOptions options, CancellationToken ct = default)
    {
        var problems = options.Validate(requiresAuth: true).ToList();
        if (!options.AllowAnyTenant && !LooksLikeTestTenant(options.TenantCode))
            problems.Add($"Tenant '{options.TenantCode}' does not look like a test tenant (its code must contain TEST, LOAD or CAP). Load runs must never touch a real clinic; set ANIMORA_LT_ALLOW_ANY_TENANT=1 only if you are certain.");
        if (problems.Count > 0)
        {
            foreach (var p in problems) Console.Error.WriteLine($"  config: {p}");
            return 78;
        }

        var profiles = LoadProfiles.All
            .Where(p => options.ProfileFilter.Length == 0 || p.Name.Contains(options.ProfileFilter, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (profiles.Count == 0)
        {
            Console.Error.WriteLine($"No profile matches '{options.ProfileFilter}'. Available: {string.Join(", ", LoadProfiles.All.Select(p => p.Name))}");
            return 64;
        }

        var levels = options.ConcurrencyLevels.Distinct().Order().ToArray();
        var runId = Convert.ToHexStringLower(Guid.NewGuid().ToByteArray()).Substring(0, 6);
        var report = new CapacityReport(runId, options.ServerBaseUrl, options.TenantCode, DateTimeOffset.UtcNow, options.P95LimitMs, options.Ssh.Enabled);

        // Device pool: bounded by the licence's device cap; extra virtual users share sessions round-robin.
        var devices = Math.Clamp(Math.Min(options.MaxDevices, levels.Max()), 1, 64);
        var clients = new List<VirtualClient>();
        try
        {
            Console.WriteLine($"Capacity run {runId}: profiles [{string.Join(", ", profiles.Select(p => p.Name))}], levels [{string.Join(",", levels)}], {devices} device session(s).");
            for (var slot = 0; slot < devices; slot++)
            {
                var client = VirtualClient.Create(options, $"w{slot:00}", slot);
                clients.Add(client);
                var error = await client.LoginAsync(ct).ConfigureAwait(false);
                if (error is not null)
                {
                    Console.Error.WriteLine($"  device {slot}: {error}");
                    if (slot == 0) return 1;
                    Console.Error.WriteLine($"  continuing with {slot} device session(s) (licence device cap reached?).");
                    clients.RemoveAt(clients.Count - 1);
                    await client.DisposeAsync().ConfigureAwait(false);
                    break;
                }
            }

            report.Devices = clients.Count;
            var pool = clients.Select(c => new PoolClient(c)).ToArray();
            var context = new LoadContext(options, runId, pool, ct);

            await using var monitor = new RemoteMonitor(options);
            if (options.Ssh.Enabled && !monitor.Start())
                Console.Error.WriteLine($"  monitor: {monitor.Problem} — continuing with client-side metrics + /health/ready only.");
            await using var health = new HealthProbe(options.ServerBaseUrl, TimeSpan.FromSeconds(1));

            foreach (var profile in profiles)
            {
                if (ct.IsCancellationRequested) break;
                Console.WriteLine($"  ▶ profile {profile.Name}: {profile.Description}");
                try
                {
                    await profile.Setup(context).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Console.Error.WriteLine($"    setup failed: {ex.Message} — profile skipped.");
                    continue;
                }

                var results = new List<LevelResult>();
                foreach (var level in levels)
                {
                    if (ct.IsCancellationRequested) break;
                    var requests = Math.Max(options.RequestsPerLevel, level * 2);
                    var from = DateTimeOffset.UtcNow;
                    var result = await LoadRunner.RunAsync(profile.Name, level, requests,
                        (worker, iteration) => profile.Operation(context, worker, iteration)).ConfigureAwait(false);
                    var to = DateTimeOffset.UtcNow;

                    var summary = MonitorAnalysis.Summarize(monitor.Snapshot(), health.Snapshot(), from, to, monitor.PgMaxConnections);
                    var (healthy, reason) = CapacityAnalysis.Judge(result, summary, options.P95LimitMs);
                    results.Add(new LevelResult(result, summary, healthy, reason));
                    Console.WriteLine($"    {level,3} users: {result.Throughput,6:0.0} req/s  p95 {result.P95,6:0} ms  fail {result.UnexpectedFailures,3}  busy {result.ExpectedBusy,3}  " +
                                      $"{(summary.ServerMetricsAvailable ? $"host cpu {summary.CpuAveragePercent:0}%/{summary.CpuPeakPercent:0}%  " : string.Empty)}" +
                                      $"{(summary.ServiceCpuAveragePercent is { } appCpu ? $"app cpu {appCpu:0}%  " : string.Empty)}" +
                                      $"{(healthy ? "OK" : "BROKEN: " + reason)}");

                    if (!healthy) break; // bounded load: never push a struggling server further.
                    if (level != levels[^1]) await Task.Delay(TimeSpan.FromSeconds(options.CooldownSeconds), ct).ConfigureAwait(false);
                }

                report.Profiles.Add(CapacityAnalysis.Conclude(profile.Name, profile.Description, results));
                await Task.Delay(TimeSpan.FromSeconds(options.CooldownSeconds), ct).ConfigureAwait(false);
            }

            report.MonitorProblem = options.Ssh.Enabled ? monitor.Problem : null;
            report.PgMaxConnections = monitor.PgMaxConnections;
            report.ServerSeries = monitor.Snapshot();
            report.HealthSeries = health.Snapshot();
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("  cancelled by the operator — writing the partial report.");
        }
        finally
        {
            foreach (var c in clients)
            {
                try { await c.LogoutAsync(CancellationToken.None).ConfigureAwait(false); }
                catch (Exception) { /* best effort */ }
                await c.DisposeAsync().ConfigureAwait(false);
            }
        }

        report.FinishedUtc = DateTimeOffset.UtcNow;
        var path = report.Write(options.OutputDir);
        Console.WriteLine();
        Console.WriteLine(report.Recommended > 0
            ? $"Recommended max concurrent virtual users: {report.Recommended}"
            : "No healthy level — see the report hints.");
        Console.WriteLine($"Report: {path}");
        return report.Passed ? 0 : 1;
    }

    /// <summary>Test tenants are named like <c>ANIMORA-LOADTEST</c>; anything else is refused unless explicitly allowed.</summary>
    public static bool LooksLikeTestTenant(string code) =>
        new[] { "TEST", "LOAD", "CAP" }.Any(marker => code.Contains(marker, StringComparison.OrdinalIgnoreCase));
}
