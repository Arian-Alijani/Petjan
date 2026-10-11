using Xunit;

namespace Animora.Server.Tests;

/// <summary>
/// Anti-escape guard for the launch bar (G09). <c>scripts/release/launch-gate.sh</c> is the single aggregator
/// that refuses a release unless every ARCH §19 gate is green; this test pins its gate list to the §19 table
/// so a future gate cannot be added to the architecture (or silently dropped from the aggregator) without
/// the launch bar following. Pure source scan — never needs a database.
/// </summary>
public sealed class LaunchGateManifestTests
{
    // ARCHITECTURE.md §19 table — the exact 18 gates (A D C N B L P O K U F S I M R Z T PG).
    private static readonly string[] Arch19Gates =
        ["A", "D", "C", "N", "B", "L", "P", "O", "K", "U", "F", "S", "I", "M", "R", "Z", "T", "PG"];

    private static readonly string[] KnownWorkflowJobs =
    [
        "pg-gates:pg-correctness", "pg-gates:pg-rls-hardening", "pg-gates:restore-drill",
        "perf-gates:offline-budget", "perf-gates:server-capacity", "perf-gates:zero-internet",
        "perf-gates:layering-audit", "perf-gates:sync-behavioral",
    ];

    private static readonly string[] KnownLocalLanes =
        ["local:smoke", "local:sbom", "local:native-inventory", "local:zero-internet-scan"];

    [Fact]
    public void EveryArch19Gate_IsCoveredByALane()
    {
        var gates = ParseGates();

        Assert.Equal(
            Arch19Gates.OrderBy(g => g, StringComparer.Ordinal),
            gates.Select(g => g.Letter).OrderBy(g => g, StringComparer.Ordinal));
        Assert.Equal(18, gates.Count);
        Assert.Equal(gates.Count, gates.Select(g => g.Letter).Distinct(StringComparer.Ordinal).Count());

        foreach (var gate in gates)
        {
            Assert.NotEmpty(gate.Lanes);
        }
    }

    [Fact]
    public void ArchitectureTable_StillListsExactlyThoseGates()
    {
        // Cross-check the hard-coded expectation against ARCHITECTURE.md §19 so the two cannot drift apart.
        var arch = File.ReadAllLines(Path.Combine(RepoRoot.Find(), "ARCHITECTURE.md"));
        var start = Array.FindIndex(arch, l => l.StartsWith("## §19", StringComparison.Ordinal));
        Assert.True(start >= 0, "ARCHITECTURE.md has no '## §19' section");

        var letters = new List<string>();
        for (var i = start + 1; i < arch.Length && !arch[i].StartsWith("## ", StringComparison.Ordinal); i++)
        {
            // Rows look like: | **A — Idempotency …** | proves … |
            var line = arch[i].TrimStart();
            if (!line.StartsWith("| **", StringComparison.Ordinal))
            {
                continue;
            }

            var inner = line[4..];
            var dash = inner.IndexOf(" — ", StringComparison.Ordinal);
            if (dash > 0)
            {
                letters.Add(inner[..dash].Trim());
            }
        }

        Assert.Equal(
            Arch19Gates.OrderBy(g => g, StringComparer.Ordinal),
            letters.OrderBy(l => l, StringComparer.Ordinal));
    }

    [Fact]
    public void EveryLane_IsAKnownWorkflowJobOrLocalLane_AndExistsInItsWorkflow()
    {
        var root = RepoRoot.Find();
        var pg = File.ReadAllText(Path.Combine(root, "pg-gates.yml"));
        var perf = File.ReadAllText(Path.Combine(root, "perf-gates.yml"));

        var lanes = ParseGates().SelectMany(g => g.Lanes).Distinct(StringComparer.Ordinal).ToArray();
        foreach (var lane in lanes)
        {
            Assert.True(
                KnownWorkflowJobs.Contains(lane, StringComparer.Ordinal) || KnownLocalLanes.Contains(lane, StringComparer.Ordinal),
                $"launch-gate.sh references an unknown lane '{lane}'");

            if (lane.StartsWith("pg-gates:", StringComparison.Ordinal))
            {
                Assert.Contains($"\n  {lane["pg-gates:".Length..]}:\n", pg);
            }
            else if (lane.StartsWith("perf-gates:", StringComparison.Ordinal))
            {
                Assert.Contains($"\n  {lane["perf-gates:".Length..]}:\n", perf);
            }
        }
    }

    [Fact]
    public void LaunchGate_RequiresSupplyChainLanes_AndFailsClosed()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot.Find(), "scripts/release/launch-gate.sh"));
        Assert.Contains("local:sbom", script, StringComparison.Ordinal);
        Assert.Contains("local:native-inventory", script, StringComparison.Ordinal);
        Assert.Contains("--dry-run", script, StringComparison.Ordinal);
        Assert.Contains("exit 1", script, StringComparison.Ordinal);
        Assert.Contains("artifacts/launch-gate-report.md", script, StringComparison.Ordinal);
    }

    private sealed record Gate(string Letter, string Meaning, string[] Lanes);

    private static List<Gate> ParseGates()
    {
        var script = File.ReadAllLines(Path.Combine(RepoRoot.Find(), "scripts/release/launch-gate.sh"));
        var gates = new List<Gate>();
        foreach (var raw in script)
        {
            var line = raw.Trim().Trim('"');
            if (!line.StartsWith("GATE|", StringComparison.Ordinal))
            {
                continue;
            }

            var parts = line.Split('|');
            Assert.Equal(4, parts.Length);
            gates.Add(new Gate(parts[1], parts[2], parts[3].Split(',', StringSplitOptions.RemoveEmptyEntries)));
        }

        return gates;
    }
}
