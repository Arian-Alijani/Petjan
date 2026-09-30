using System;
using System.Linq;
using System.Reflection;
using Xunit;

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
}
