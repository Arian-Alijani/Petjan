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
}
