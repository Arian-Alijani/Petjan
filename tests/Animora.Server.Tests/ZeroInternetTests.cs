using System.Text.RegularExpressions;
using Xunit;

namespace Animora.Server.Tests;

/// <summary>
/// Zero-Internet gate Z (G08, ARCH §17.4 / §19). The heavy legs live in <c>scripts/perf/zero-internet.sh</c>
/// (static built-output scan + desktop smoke with egress blocked, run by the <c>zero-internet</c> job of
/// <c>perf-gates.yml</c>). These tests are the in-suite guard: the source tree must not embed CDN / font /
/// analytics endpoints, the script must keep its two legs, and — when the script's smoke log is supplied
/// via <c>ANIMORA_ZERO_INTERNET_LOG</c> — the runtime leg must have reported <c>SMOKE RUN OK</c>.
/// </summary>
public sealed class ZeroInternetTests
{
    private const string SmokeToken = "SMOKE RUN OK";

    private static readonly Regex ForbiddenHost = new(
        @"https?://(?:[a-z0-9.-]*\.)?(?:fonts\.googleapis\.com|fonts\.gstatic\.com|googleapis\.com|gstatic\.com|cdnjs\.cloudflare\.com|cdn\.jsdelivr\.net|jsdelivr\.net|unpkg\.com|cloudfront\.net|bootstrapcdn\.com|googletagmanager\.com|google-analytics\.com|cdn[a-z0-9.-]*\.[a-z]{2,})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly string[] ClientSourceRoots =
    [
        "src/Animora.Desktop",
        "src/Animora.Client.Services",
        "src/Animora.Infrastructure.Local",
        "src/Animora.Sync.Custom",
    ];

    private static readonly string[] ScannedExtensions = [".axaml", ".xaml", ".cs", ".json", ".css", ".html", ".resx"];

    [Fact]
    public void Runtime_OperatesWithEgressBlocked()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot.Find(), "scripts/perf/zero-internet.sh"));

        // The script must keep the runtime leg: egress-blocked namespace + the smoke token requirement.
        Assert.Contains("unshare", script, StringComparison.Ordinal);
        Assert.Contains("scripts/sandbox/run.sh", script, StringComparison.Ordinal);
        Assert.Contains(SmokeToken, script, StringComparison.Ordinal);

        // When CI hands us the runtime leg's log, it must actually report success.
        var log = Environment.GetEnvironmentVariable("ANIMORA_ZERO_INTERNET_LOG");
        if (string.IsNullOrWhiteSpace(log))
        {
            return;
        }

        var path = Path.IsPathRooted(log) ? log : Path.Combine(RepoRoot.Find(), log);
        Assert.True(File.Exists(path), $"ANIMORA_ZERO_INTERNET_LOG points at a missing file: {path}");
        Assert.Contains(SmokeToken, File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void StaticScan_Script_ForbidsCdnAndFontHosts()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot.Find(), "scripts/perf/zero-internet.sh"));
        foreach (var token in new[] { "fonts.googleapis.com", "cdnjs", "jsdelivr", "unpkg.com", "is_forbidden_host" })
        {
            Assert.Contains(token, script, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ClientSources_EmbedNoCdnFontOrAnalyticsEndpoints()
    {
        var root = RepoRoot.Find();
        var offenders = new List<string>();

        foreach (var rel in ClientSourceRoots)
        {
            var dir = Path.Combine(root, rel);
            if (!Directory.Exists(dir))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                var sep = Path.DirectorySeparatorChar;
                if (file.Contains($"{sep}bin{sep}", StringComparison.Ordinal) || file.Contains($"{sep}obj{sep}", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!ScannedExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                var match = ForbiddenHost.Match(File.ReadAllText(file));
                if (match.Success)
                {
                    offenders.Add($"{Path.GetRelativePath(root, file)}: {match.Value}");
                }
            }
        }

        Assert.True(offenders.Count == 0, "CDN/font/analytics endpoints found:\n" + string.Join("\n", offenders));
    }
}
