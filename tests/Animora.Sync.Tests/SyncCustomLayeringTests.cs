using Animora.Sync.Custom;
using Xunit;

namespace Animora.Sync.Tests;

/// <summary>Layering facts for <c>Animora.Sync.Custom</c> (R-B10): no server, PostgreSQL or Datasync references; no push path.</summary>
public sealed class SyncCustomLayeringTests
{
    [Fact]
    public void SyncCustom_HasNoServerOrPostgresOrDatasyncReferences()
    {
        var names = typeof(HttpStateSyncEngine).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty).ToList();

        foreach (var prefix in new[] { "Npgsql", "Animora.Server", "Animora.Persistence.PostgreSql", "CommunityToolkit.Datasync", "Animora.Desktop" })
            Assert.DoesNotContain(names, n => n.StartsWith(prefix, StringComparison.Ordinal));
    }

    [Fact]
    public void HttpEngine_ExposesNoPushSurface()
    {
        Assert.DoesNotContain(typeof(HttpStateSyncEngine).GetMethods(), m => m.Name.Contains("Push", StringComparison.Ordinal));
    }
}
