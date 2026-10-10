using Xunit;

namespace Animora.Server.Tests.Subscriptions;

public sealed class SubscriptionLayeringTests
{
    [Fact]
    public void ClientProjects_DoNotReferenceSubscriptions()
    {
        var root = RepoRoot.Find();
        string[] clients = ["Animora.Client.Services", "Animora.Desktop", "Animora.Persistence.Sqlite", "Animora.Infrastructure.Local"];
        foreach (var client in clients)
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src", client), "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
            Assert.DoesNotContain("Animora.Domain.Subscriptions", File.ReadAllText(file), StringComparison.Ordinal);
    }
}
