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
                Console.Error.WriteLine("capacity suite not yet wired (task T6).");
                return 2;
            case "all":
            {
                var rc = await Correctness.CorrectnessRunner.RunAsync(options, cts.Token);
                Console.Error.WriteLine("capacity suite not yet wired (task T6).");
                return rc != 0 ? rc : 2;
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
