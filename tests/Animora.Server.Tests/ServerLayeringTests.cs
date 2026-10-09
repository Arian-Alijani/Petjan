using System.Reflection;
using Animora.Server.Hosting;
using Xunit;

namespace Animora.Server.Tests;

public sealed class ServerLayeringTests
{
    private static readonly string[] ForbiddenClientAssemblies =
    [
        "Animora.Client.Services",
        "Animora.Persistence.Sqlite",
        "Animora.Infrastructure.Local",
        "Animora.Desktop",
        "Animora.Sync.Custom",
    ];

    [Fact]
    public void Server_DoesNotReferenceClientProjects()
    {
        var server = typeof(ServerRoutes).Assembly;

        var referenced = server.GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(n => n is not null)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var forbidden in ForbiddenClientAssemblies)
        {
            Assert.DoesNotContain(forbidden, referenced);
        }
    }

    [Fact]
    public void Server_ReferencesOnlyAllowedAnimoraAssemblies()
    {
        string[] allowed = ["Animora.Domain", "Animora.Application", "Animora.Contracts.Wire"];

        var animoraRefs = typeof(ServerRoutes).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(n => n.StartsWith("Animora.", StringComparison.Ordinal))
            .ToArray();

        Assert.All(animoraRefs, n => Assert.Contains(n, allowed));
    }

    [Fact]
    public void Server_HasNoPostgresReference_InS01()
    {
        Assert.DoesNotContain(
            typeof(ServerRoutes).Assembly.GetReferencedAssemblies(),
            a => a.Name is not null && a.Name.StartsWith("Npgsql", StringComparison.Ordinal));
    }
}
