using System.Net.Http.Json;
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
    /// Read-only reachability check: GET /health/ready. Prints a clear PASS/FAIL summary so the
    /// operator confirms connectivity before any heavy run. Auth is validated in task T3.
    /// </summary>
    private static async Task<int> PreflightAsync(LiveTestOptions options)
    {
        var problems = options.Validate(requiresAuth: false);
        if (problems.Count > 0)
        {
            foreach (var p in problems) Console.Error.WriteLine($"  config: {p}");
            return 78;
        }

        using var http = new HttpClient
        {
            BaseAddress = new Uri(options.ServerBaseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds),
        };

        try
        {
            using var resp = await http.GetAsync("health/ready");
            var body = await resp.Content.ReadAsStringAsync();
            string status = "unknown";
            try
            {
                var doc = await resp.Content.ReadFromJsonAsync<HealthBody>();
                status = doc?.Status ?? "unknown";
            }
            catch { /* non-JSON body is still reported via raw text below */ }

            Console.WriteLine($"  /health/ready → HTTP {(int)resp.StatusCode} status='{status}'");
            if (resp.IsSuccessStatusCode && (status is "ready" or "degraded"))
            {
                Console.WriteLine("PREFLIGHT PASS — server reachable and ready.");
                return 0;
            }

            Console.Error.WriteLine($"PREFLIGHT FAIL — server not ready. Body: {Trim(body)}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"PREFLIGHT FAIL — cannot reach {options.ServerBaseUrl}: {ex.Message}");
            return 1;
        }
    }

    private static string Trim(string s) => s.Length <= 300 ? s : s[..300] + "…";

    private sealed record HealthBody(string? Status);
}
