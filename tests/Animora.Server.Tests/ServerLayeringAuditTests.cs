using System.Xml.Linq;
using Xunit;

namespace Animora.Server.Tests;

/// <summary>
/// Layering audit (G08, ARCH §19 gate P — three planes / R-B10). Complements <see cref="ServerLayeringTests"/>
/// (compiled-assembly reference sets) with a project-file (<c>.csproj</c>) graph audit, so projects that the
/// Server test assembly does not itself reference (client side, Sync.Custom) are covered too.
/// Pure source scan — never needs a database.
/// </summary>
public sealed class ServerLayeringAuditTests
{
    private static readonly string[] ServerProjects =
    [
        "Animora.Server",
        "Animora.Persistence.PostgreSql",
        "Animora.Server.Licensing",
        "Animora.Web.Admin",
    ];

    private static readonly string[] ClientProjects =
    [
        "Animora.Client.Services",
        "Animora.Persistence.Sqlite",
        "Animora.Infrastructure.Local",
        "Animora.Desktop",
        "Animora.Sync.Custom",
    ];

    [Fact]
    public void NoClientProjectReferencesAServerProject_ExceptLicensingVerification()
    {
        foreach (var client in ClientProjects)
        {
            foreach (var reference in ProjectReferences(client))
            {
                if (!ServerProjects.Contains(reference, StringComparer.Ordinal))
                {
                    continue;
                }

                // The ONLY sanctioned crossing: the licence-verification half (offline envelope check).
                Assert.True(
                    reference == "Animora.Server.Licensing",
                    $"{client} must not reference server project {reference} (only Animora.Server.Licensing verification is allowed).");
            }
        }

        // Persistence.Sqlite is the pure local store — it may not even take the licensing crossing.
        Assert.DoesNotContain(ProjectReferences("Animora.Persistence.Sqlite"), r => ServerProjects.Contains(r, StringComparer.Ordinal));
    }

    [Fact]
    public void AnimoraServer_DoesNotReference_PersistenceSqlite_Or_Desktop()
    {
        var refs = ProjectReferences("Animora.Server");
        Assert.DoesNotContain("Animora.Persistence.Sqlite", refs);
        Assert.DoesNotContain("Animora.Desktop", refs);
        Assert.DoesNotContain("Animora.Client.Services", refs);
        Assert.DoesNotContain("Animora.Infrastructure.Local", refs);

        // Transitively as well: walk the project-reference closure of the server host.
        var closure = Closure("Animora.Server");
        foreach (var forbidden in ClientProjects)
        {
            Assert.DoesNotContain(forbidden, closure);
        }
    }

    [Fact]
    public void OnlyPersistencePostgreSql_ReferencesNpgsql()
    {
        foreach (var project in Directory.EnumerateDirectories(Path.Combine(RepoRoot.Find(), "src"))
                     .Select(d => Path.GetFileName(d)!))
        {
            var csproj = Path.Combine(RepoRoot.Find(), "src", project, project + ".csproj");
            if (!File.Exists(csproj))
            {
                continue;
            }

            var packages = XDocument.Load(csproj).Descendants("PackageReference")
                .Select(p => p.Attribute("Include")?.Value ?? string.Empty)
                .Where(n => n.StartsWith("Npgsql", StringComparison.Ordinal))
                .ToArray();

            if (project == "Animora.Persistence.PostgreSql")
            {
                Assert.NotEmpty(packages);
            }
            else
            {
                Assert.True(packages.Length == 0, $"{project} must not reference Npgsql ({string.Join(",", packages)}).");
            }
        }
    }

    [Fact]
    public void SyncCustom_HasNoNpgsql_NoServerRef()
    {
        var doc = XDocument.Load(ProjectFile("Animora.Sync.Custom"));
        var packages = doc.Descendants("PackageReference").Select(p => p.Attribute("Include")?.Value ?? string.Empty).ToArray();
        Assert.DoesNotContain(packages, n => n.StartsWith("Npgsql", StringComparison.Ordinal));
        Assert.DoesNotContain(packages, n => n.StartsWith("CommunityToolkit.Datasync", StringComparison.Ordinal));

        var closure = Closure("Animora.Sync.Custom");
        foreach (var server in ServerProjects)
        {
            Assert.DoesNotContain(server, closure);
        }

        Assert.DoesNotContain("Animora.Desktop", closure);
    }

    private static string ProjectFile(string project) =>
        Path.Combine(RepoRoot.Find(), "src", project, project + ".csproj");

    private static string[] ProjectReferences(string project) =>
        XDocument.Load(ProjectFile(project)).Descendants("ProjectReference")
            .Select(r => Path.GetFileNameWithoutExtension((r.Attribute("Include")?.Value ?? string.Empty).Replace('\\', '/')))
            .ToArray();

    private static HashSet<string> Closure(string root)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var current))
        {
            foreach (var reference in ProjectReferences(current))
            {
                if (seen.Add(reference))
                {
                    pending.Push(reference);
                }
            }
        }

        return seen;
    }
}
