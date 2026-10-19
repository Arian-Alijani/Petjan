using Animora.LiveTest.Config;

namespace Animora.LiveTest;

/// <summary>
/// Entry point. Modes:
///   preflight    connectivity + auth sanity check (safe, read-only)
///   correctness  per-feature real create/edit + 2-client sync convergence suite
///   capacity     realistic concurrent load + read-only remote hardware monitor
///   all          correctness then capacity
/// Every mode drives the LIVE server over real HTTP against the dedicated test tenant.
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        string mode = (args.FirstOrDefault() ?? "preflight").ToLowerInvariant();
        var options = LiveTestOptions.Resolve();

        Console.WriteLine($"Animora LiveTest — mode '{mode}' — server {options.ServerBaseUrl}");

        // Ctrl+C stops the run gracefully so the report is still written and cleanup still runs.
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        switch (mode)
        {
            case "preflight":
                return await PreflightAsync(options);
            case "correctness":
                return await Correctness.CorrectnessRunner.RunAsync(options, cts.Token);
            case "capacity":
                return await Capacity.CapacityRunner.RunAsync(options, cts.Token);
            case "all":
            {
                // Correctness first: do not load a server that already fails functionally.
                var rc = await Correctness.CorrectnessRunner.RunAsync(options, cts.Token);
                if (rc != 0)
                {
                    Console.Error.WriteLine("correctness failed — capacity run skipped.");
                    return rc;
                }

                return await Capacity.CapacityRunner.RunAsync(options, cts.Token);
            }
            default:
                Console.Error.WriteLine($"unknown mode '{mode}'. Use: preflight | correctness | capacity | all");
                return 64;
        }
    }

    /// <summary>
    /// Read-only connectivity + auth check (health → login → licence → sync manifest → logout). Prints a PASS/FAIL
    /// line per step so the operator confirms the whole path before any heavy run.
    /// </summary>
    private static Task<int> PreflightAsync(LiveTestOptions options) => Client.Preflight.RunAsync(options);
}
