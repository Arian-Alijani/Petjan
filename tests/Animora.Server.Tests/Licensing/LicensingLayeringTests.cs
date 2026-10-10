using System.Reflection;
using System.Xml.Linq;
using Animora.Server.Licensing.Verification;
using Xunit;
namespace Animora.Server.Tests.Licensing;

public sealed class LicensingLayeringTests
{
    [Fact]
    public void VerificationHalf_ClosureHasNoServerAssemblies()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal); var pending = new Stack<Assembly>();
        pending.Push(typeof(LicenseEnvelope).Assembly);
        while (pending.TryPop(out var assembly))
        {
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                var name = reference.Name ?? string.Empty;
                Assert.False(name.StartsWith("Npgsql", StringComparison.Ordinal) || name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
                    || name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal), name);
                if (name.StartsWith("Animora.", StringComparison.Ordinal) && seen.Add(name)) pending.Push(Assembly.Load(reference));
            }
        }
        foreach (var path in Directory.EnumerateFiles(Path.Combine(RepoRoot.Find(), "src/Animora.Server.Licensing/Verification"), "*.cs"))
            Assert.DoesNotContain("using Animora.Application", File.ReadAllText(path));
    }
    [Fact]
    public void OnlyLicensingProject_WhitelistedForClients()
    {
        foreach (var project in new[] { "Animora.Client.Services", "Animora.Desktop", "Animora.Infrastructure.Local", "Animora.Persistence.Sqlite" })
        {
            var doc = XDocument.Load(Path.Combine(RepoRoot.Find(), "src", project, project + ".csproj"));
            foreach (var reference in doc.Descendants("ProjectReference"))
            {
                var path = reference.Attribute("Include")?.Value ?? string.Empty;
                if (path.Contains("Animora.Server", StringComparison.Ordinal))
                { Assert.NotEqual("Animora.Persistence.Sqlite", project); Assert.Contains("Animora.Server.Licensing.csproj", path); }
            }
        }
    }
}
