using Xunit;

namespace Animora.Server.Tests;

public sealed class SourceScanTests
{
    private static IEnumerable<string> ServerSources()
    {
        var dir = Path.Combine(RepoRoot.Find(), "src", "Animora.Server");
        return Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                        !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
    }

    [Fact]
    public void No_HardCoded_SslProtocols()
    {
        var sources = ServerSources().ToArray();
        Assert.NotEmpty(sources);

        foreach (var file in sources)
        {
            Assert.DoesNotContain("SslProtocols.", File.ReadAllText(file), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void No_Npgsql_InServerSources()
    {
        foreach (var file in ServerSources())
        {
            Assert.DoesNotContain("Npgsql", File.ReadAllText(file), StringComparison.Ordinal);
        }
    }

    // ── S02 guards ───────────────────────────────────────────────────────────

    private static IEnumerable<string> SourcesUnder(params string[] projects) =>
        projects.SelectMany(project =>
            Directory.EnumerateFiles(Path.Combine(RepoRoot.Find(), "src", project), "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                        !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    // The unnamed overload is `IgnoreQueryFilters()`; the interceptor that bans it is the only
    // file allowed to mention the literal (inside doc/exception text, never as a call).
    [Fact]
    public void No_UnnamedIgnoreQueryFilters_Calls()
    {
        foreach (var file in SourcesUnder("Animora.Persistence.PostgreSql", "Animora.Server"))
        {
            if (file.EndsWith("NoUnnamedIgnoreFiltersInterceptor.cs", StringComparison.Ordinal))
            {
                continue;
            }

            Assert.DoesNotContain(".IgnoreQueryFilters()", File.ReadAllText(file), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void No_RowVersion_Or_Xmin_InPostgreSqlPersistence()
    {
        foreach (var file in SourcesUnder("Animora.Persistence.PostgreSql"))
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("IsRowVersion", text, StringComparison.Ordinal);
            Assert.DoesNotContain("xmin", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void No_LegacyTimestampBehavior_AnywhereInSrc()
    {
        var src = Path.Combine(RepoRoot.Find(), "src");
        var files = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                        !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

        foreach (var file in files)
        {
            Assert.DoesNotContain("EnableLegacyTimestampBehavior", File.ReadAllText(file), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Npgsql_IsConfinedToPostgreSqlProject()
    {
        var src = Path.Combine(RepoRoot.Find(), "src");
        var offenders = Directory.EnumerateFiles(src, "*.csproj", SearchOption.AllDirectories)
            .Where(f => !Path.GetFileName(f).Equals("Animora.Persistence.PostgreSql.csproj", StringComparison.Ordinal))
            .Where(f => File.ReadAllText(f).Contains("Npgsql", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(offenders);
    }
}
