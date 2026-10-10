using Animora.Sync.Abstractions;
using Xunit;

namespace Animora.Sync.Tests;

/// <summary>Layering facts for <c>Animora.Sync.Abstractions</c>.</summary>
public sealed class SyncLayeringTests
{
    [Fact]
    public void SyncAbstractions_ReferencesOnlyDomainWireAndFramework()
    {
        var names = typeof(IStateSyncEngine).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty).ToList();

        string[] forbidden =
        [
            "Npgsql", "Microsoft.EntityFrameworkCore", "System.Net.Http",
            "Animora.Persistence", "Animora.Application", "Animora.Server",
        ];
        foreach (var prefix in forbidden)
            Assert.DoesNotContain(names, n => n.StartsWith(prefix, StringComparison.Ordinal));

        // Animora.Contracts.Wire is a declared project reference but the compiler elides it until a wire type is used.
        Assert.Contains("Animora.Domain", names);
    }
}
