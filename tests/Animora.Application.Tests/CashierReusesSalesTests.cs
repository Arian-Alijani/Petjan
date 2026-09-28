using System.Reflection;
using Animora.Application.Abstractions;
using Animora.Client.Services.Cashier;
using Xunit;

namespace Animora.Application.Tests;

/// <summary>
/// The golden rule of C08: <c>CashierService</c> reuses the C07 sales commands verbatim and owns no
/// persistence of its own. This guard scans every type under the
/// <c>Animora.Client.Services.Cashier</c> namespace and fails if any of them so much as references a
/// unit of work, a repository port or the local <c>DbContext</c> in a member signature — the surest
/// sign that write logic had started to leak down into the client layer.
/// </summary>
public sealed class CashierReusesSalesTests
{
    private const BindingFlags All =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    // Names, not hard type refs: the persistence assembly need not be reachable to name what is banned.
    private static readonly string[] Forbidden =
    [
        "IUnitOfWork",
        "Repository",           // any *Repository port
        "AnimoraLocalDbContext",
        "DbContext",
    ];

    private static IEnumerable<Type> CashierTypes() =>
        typeof(CashierService).Assembly.GetTypes()
            .Where(t => t.Namespace == "Animora.Client.Services.Cashier");

    [Fact]
    public void CashierNamespace_HasTypes()
    {
        // A silent empty scan would make the whole guard vacuous.
        Assert.Contains(CashierTypes(), t => t == typeof(CashierService));
    }

    [Fact]
    public void CashierTypes_ReferenceNoPersistenceContracts()
    {
        foreach (var type in CashierTypes())
        {
            foreach (var f in type.GetFields(All))
                AssertClean(type, f.Name, f.FieldType);
            foreach (var p in type.GetProperties(All))
                AssertClean(type, p.Name, p.PropertyType);
            foreach (var ctor in type.GetConstructors(All))
                foreach (var p in ctor.GetParameters())
                    AssertClean(type, $"ctor {p.Name}", p.ParameterType);
            foreach (var m in type.GetMethods(All))
            {
                AssertClean(type, m.Name, m.ReturnType);
                foreach (var p in m.GetParameters())
                    AssertClean(type, $"{m.Name}/{p.Name}", p.ParameterType);
            }
        }
    }

    private static void AssertClean(Type owner, string member, Type dependency)
    {
        var name = dependency.Name;
        foreach (var banned in Forbidden)
        {
            Assert.False(name.Contains(banned, StringComparison.Ordinal),
                $"{owner.Name}.{member} references '{name}', which contains the forbidden token '{banned}'. " +
                "Cashier must reuse the C07 sales commands, not own persistence.");
        }
    }
}
