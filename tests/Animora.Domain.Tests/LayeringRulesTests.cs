using System;
using System.Linq;
using System.Reflection;
using Xunit;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Domain.Tests;

/// <summary>
/// Enforces the dependency-direction rules of CONVENTIONS.md §2 by asserting on
/// each subject assembly's referenced-assembly set. Later phases EXTEND this
/// class (adding facts / tightening scope); they never weaken an existing fact.
///
/// Each fact touches one <c>typeof(...)</c> from the asserted assembly so the
/// assembly is deterministically loaded before <c>GetReferencedAssemblies</c>
/// is read.
/// </summary>
public sealed class LayeringRulesTests
{
    private static AssemblyName[] ReferencedAssembliesOf(Assembly assembly)
        => assembly.GetReferencedAssemblies();

    private static bool ReferencesNameStartingWith(Assembly assembly, string prefix)
        => ReferencedAssembliesOf(assembly)
            .Any(a => a.Name is not null &&
                      a.Name.StartsWith(prefix, StringComparison.Ordinal));

    /// <summary>
    /// Animora.Domain references no other <c>Animora.*</c> assembly, and no
    /// EF Core or Datasync assembly. Domain must have zero dependencies.
    /// </summary>
    [Fact]
    public void Domain_HasNoForbiddenReferences()
    {
        var domain = typeof(Animora.Domain.AssemblyMarker).Assembly;

        var animoraRefs = ReferencedAssembliesOf(domain)
            .Where(a => a.Name is not null &&
                        a.Name.StartsWith("Animora.", StringComparison.Ordinal) &&
                        !string.Equals(a.Name, "Animora.Domain", StringComparison.Ordinal))
            .Select(a => a.Name)
            .ToArray();

        Assert.Empty(animoraRefs);
        Assert.False(ReferencesNameStartingWith(domain, "Microsoft.EntityFrameworkCore"));
        Assert.False(ReferencesNameStartingWith(domain, "CommunityToolkit.Datasync"));
    }

    /// <summary>
    /// Animora.Application references neither EF Core nor Datasync. It may only
    /// depend on Domain, Contracts (DTOs), and Contracts.Wire (CONVENTIONS.md §2).
    /// </summary>
    [Fact]
    public void Application_HasNoInfrastructureReferences()
    {
        var application = typeof(Animora.Application.AssemblyMarker).Assembly;

        Assert.False(ReferencesNameStartingWith(application, "Microsoft.EntityFrameworkCore"));
        Assert.False(ReferencesNameStartingWith(application, "CommunityToolkit.Datasync"));
    }

    /// <summary>
    /// Animora.Contracts references no <c>Animora.*</c> assembly — it is the
    /// pure desktop-facing seam and must not depend on any product assembly.
    /// </summary>
    [Fact]
    public void Contracts_ReferencesNoAnimoraAssembly()
    {
        var contracts = typeof(Animora.Contracts.Navigation.UserRole).Assembly;

        var animoraRefs = ReferencedAssembliesOf(contracts)
            .Where(a => a.Name is not null &&
                        a.Name.StartsWith("Animora.", StringComparison.Ordinal) &&
                        !string.Equals(a.Name, "Animora.Contracts", StringComparison.Ordinal))
            .Select(a => a.Name)
            .ToArray();

        Assert.Empty(animoraRefs);
    }

    /// <summary>
    /// B03c: the ASP.NET Core Identity hashing package is referenced by
    /// <c>Animora.Infrastructure.Local</c> only (CONVENTIONS §2/§7). Checked on the
    /// actual referenced assembly names (<c>Microsoft.Extensions.Identity.Core</c>
    /// hosts <c>Microsoft.AspNetCore.Identity.PasswordHasher</c>).
    /// </summary>
    [Fact]
    public void IdentityPackage_IsReferencedOnlyByInfrastructureLocal()
    {
        static bool ReferencesIdentity(Assembly assembly) =>
            ReferencedAssembliesOf(assembly).Any(a => a.Name is not null &&
                (a.Name.StartsWith("Microsoft.Extensions.Identity", StringComparison.Ordinal) ||
                 a.Name.StartsWith("Microsoft.AspNetCore.Identity", StringComparison.Ordinal)));

        var infrastructure = typeof(Animora.Infrastructure.Local.LocalInfrastructureServiceCollectionExtensions).Assembly;
        Assert.True(ReferencesIdentity(infrastructure));

        Assert.False(ReferencesIdentity(typeof(Animora.Domain.AssemblyMarker).Assembly));
        Assert.False(ReferencesIdentity(typeof(Animora.Application.AssemblyMarker).Assembly));
        Assert.False(ReferencesIdentity(typeof(Animora.Contracts.Navigation.UserRole).Assembly));
        Assert.False(ReferencesIdentity(
            typeof(Animora.Persistence.Sqlite.Context.LocalStoreServiceCollectionExtensions).Assembly));
        Assert.False(ReferencesIdentity(typeof(Animora.Client.Services.Identity.AuthenticationService).Assembly));
    }
    [Fact]
    public void NoMockNamespaceExists()
    {
        var types = typeof(Animora.Desktop.App).Assembly.GetTypes();
        Assert.DoesNotContain(types, t => t.Name.StartsWith("Mock", StringComparison.Ordinal));
        Assert.DoesNotContain(types, t => t.Namespace?.Split('.').Contains("Mocks") == true);
    }

    [Fact]
    public void Contracts_HaveExactlyOneRealRegistration()
    {
        var services = new ServiceCollection();
        Animora.Desktop.App.ConfigureServices(services);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var interfaces = typeof(Animora.Contracts.Sync.ISyncStatusService).Assembly.GetExportedTypes()
            .Where(t => t.IsInterface).ToArray();
        Assert.NotEmpty(interfaces);
        // UI-only interfaces live in Desktop, not Contracts. The one exception is an optional, deliberately
        // unregistered seam that UserProfileService resolves with GetService (server-supplied account status; offline = absent).
        foreach (var contract in interfaces.Where(t => t != typeof(Animora.Contracts.Profile.IClinicAccountStatusSource)))
        {
            // Offline TryAdd fallbacks (Null*) registered by lower layers are not "real" registrations.
            var registrations = services
                .Where(d => d.ServiceType == contract && d.ImplementationType?.Name.StartsWith("Null", StringComparison.Ordinal) != true)
                .ToArray();
            Assert.True(registrations.Length == 1, $"{contract.FullName} has {registrations.Length} registrations (expected exactly 1).");
            var descriptor = registrations[0];
            var implementation = descriptor.ImplementationType ?? provider.GetRequiredService(contract).GetType();
            Assert.Equal("Animora.Client.Services", implementation.Assembly.GetName().Name);
        }
    }

    [Fact]
    public void Desktop_HasNoStoreOrNetworkReferences()
    {
        var desktop = typeof(Animora.Desktop.App).Assembly;
        foreach (var prefix in new[] { "Microsoft.EntityFrameworkCore", "System.Data.Common",
                     "System.Net.Http", "CommunityToolkit.Datasync" })
            Assert.False(ReferencesNameStartingWith(desktop, prefix), prefix);
        var client = typeof(Animora.Client.Services.Sync.LocalSyncStatusService).Assembly;
        Assert.False(ReferencesNameStartingWith(client, "Microsoft.EntityFrameworkCore"));
    }

    /// <summary>
    /// S01: the client never references a server project (client/server isolation,
    /// CONVENTIONS §2). Checked on assembly-name strings — no project reference to
    /// the server is needed.
    /// </summary>
    [Fact]
    public void Client_DoesNotReferenceServerProjects()
    {
        var clientAssemblies = new[]
        {
            typeof(Animora.Desktop.App).Assembly,
            typeof(Animora.Client.Services.Sync.LocalSyncStatusService).Assembly,
            typeof(Animora.Infrastructure.Local.LocalInfrastructureServiceCollectionExtensions).Assembly,
            typeof(Animora.Persistence.Sqlite.Context.LocalStoreServiceCollectionExtensions).Assembly,
        };

        foreach (var assembly in clientAssemblies)
        {
            foreach (var prefix in new[] { "Animora.Server", "Animora.Persistence.PostgreSql", "Animora.Web.Admin" })
            {
                var prohibited = ReferencedAssembliesOf(assembly).Where(a => a.Name is not null && a.Name.StartsWith(prefix, StringComparison.Ordinal))
                    .Where(a => !(a.Name == "Animora.Server.Licensing" && assembly.GetName().Name is
                        "Animora.Client.Services" or "Animora.Desktop" or "Animora.Infrastructure.Local"));
                Assert.Empty(prohibited);
            }
        }
    }
}
