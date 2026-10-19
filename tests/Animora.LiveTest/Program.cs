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

        switch (mode)
        {
            case "preflight":
                return await PreflightAsync(options);
            case "correctness":
                Console.Error.WriteLine("correctness suite not yet wired (task T4).");
                return 2;
            case "capacity":
                Console.Error.WriteLine("capacity suite not yet wired (task T6).");
                return 2;
            case "all":
                Console.Error.WriteLine("correctness/capacity suites not yet wired (tasks T4/T6).");
                return 2;
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
