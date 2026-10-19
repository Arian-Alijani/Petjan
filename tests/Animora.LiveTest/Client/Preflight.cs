using System.Net;
using System.Text.Json;
using Animora.LiveTest.Config;

namespace Animora.LiveTest.Client;

/// <summary>
/// Read-only connectivity + auth sanity check run before any heavy suite: health → login → licence status →
/// sync manifest → logout. Every step prints one PASS/FAIL line; it creates no business data.
/// </summary>
public static class Preflight
{
    /// <summary>Runs the checks; returns the process exit code (0 = all green).</summary>
    public static async Task<int> RunAsync(LiveTestOptions options, CancellationToken ct = default)
    {
        var problems = options.Validate(requiresAuth: true);
        if (problems.Count > 0)
        {
            foreach (var p in problems) Console.Error.WriteLine($"  config: {p}");
            return 78;
        }

        var failures = 0;
        await using var client = VirtualClient.Create(options, "preflight", slot: 0);

        // 1. Anonymous readiness probe.
        var (status, body, elapsed) = await client.GetAsync("health/ready", ct);
        failures += Report("health/ready", status is HttpStatusCode.OK, $"HTTP {Describe(status)} in {elapsed.TotalMilliseconds:F0} ms — {Trim(body)}");
        if (status is null) return Finish(failures + 1);

        // 2. Login (also registers/labels the device row).
        var loginError = await client.LoginAsync(ct);
        failures += Report("auth/login", loginError is null,
            loginError ?? $"role={client.Role} tenant={client.TenantId} device={client.DeviceId}");
        if (loginError is not null) return Finish(failures);

        // 3. Licence status of the test tenant (must be granted or business routes answer 403).
        (status, body, elapsed) = await client.GetAsync("licensing/status", ct);
        var licenceOk = status is HttpStatusCode.OK && !body.Contains("NotGranted", StringComparison.OrdinalIgnoreCase);
        failures += Report("licensing/status", licenceOk,
            licenceOk ? $"{Trim(body)}" : $"HTTP {Describe(status)} — tenant not licensed? run scripts/grant-test-subscription.sh. {Trim(body)}");

        // 4. Authorized sync manifest (proves bearer + tenant membership + active device).
        (status, body, elapsed) = await client.GetAsync("sync/manifest", ct);
        var manifestOk = status is HttpStatusCode.OK && IsManifest(body);
        failures += Report("sync/manifest", manifestOk, manifestOk ? $"in {elapsed.TotalMilliseconds:F0} ms — {ManifestSummary(body)}" : $"HTTP {Describe(status)} — {Trim(body)}");

        // 5. Clean sign-out (best effort, so the test tenant keeps no dangling refresh token).
        await client.LogoutAsync(ct);
        failures += Report("auth/logout", true, "session dropped");

        return Finish(failures);
    }

    private static int Finish(int failures)
    {
        Console.WriteLine(failures == 0
            ? "PREFLIGHT PASS — server reachable, test account signed in, tenant licensed, sync feed authorized."
            : $"PREFLIGHT FAIL — {failures} check(s) failed (see above).");
        return failures == 0 ? 0 : 1;
    }

    private static int Report(string name, bool ok, string detail)
    {
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {name,-18} {detail}");
        return ok ? 0 : 1;
    }

    private static bool IsManifest(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty("epoch", out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string ManifestSummary(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        string Get(string n) => root.TryGetProperty(n, out var v) ? v.ToString() : "?";
        var projections = root.TryGetProperty("projectionOrder", out var p) && p.ValueKind == JsonValueKind.Array ? p.GetArrayLength() : 0;
        return $"contract={Get("contractVersion")} epoch={Get("epoch")} generation={Get("generation")} projections={projections}";
    }

    private static string Describe(HttpStatusCode? status) => status is null ? "(no response)" : ((int)status).ToString();

    private static string Trim(string s)
    {
        s = s.ReplaceLineEndings(" ");
        return s.Length <= 200 ? s : s[..200] + "…";
    }
}
