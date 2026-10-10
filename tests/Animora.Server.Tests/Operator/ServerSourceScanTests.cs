using Xunit;

namespace Animora.Server.Tests.Operator;

public sealed class ServerSourceScanTests
{
    [Fact]
    public void OperatorContext_OnlyUsedInOperatorAndSubscriptionNamespaces()
    {
        var root = Path.Combine(RepoRoot.Find(), "src", "Animora.Server");
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(x => !x.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
        {
            if (!File.ReadAllText(file).Contains("AnimoraOperatorDbContext", StringComparison.Ordinal)) continue;
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            Assert.True(relative.StartsWith("Operator/", StringComparison.Ordinal) || relative.StartsWith("Subscriptions/", StringComparison.Ordinal) ||
                relative.StartsWith("Admin/", StringComparison.Ordinal) || relative.StartsWith("Insight/", StringComparison.Ordinal) || relative.StartsWith("Licensing/", StringComparison.Ordinal), relative);
        }
    }
}
