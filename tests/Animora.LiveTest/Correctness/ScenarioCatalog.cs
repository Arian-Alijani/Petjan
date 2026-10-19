namespace Animora.LiveTest.Correctness;

/// <summary>One named, self-contained correctness scenario.</summary>
public sealed record ScenarioDef(string Feature, string Name, Func<ScenarioContext, Task> Run);

/// <summary>
/// Ordered list of every correctness scenario. Order matters only for readability: shared fixtures are created lazily,
/// so any scenario can also run alone via <c>ANIMORA_LT_FILTER</c>.
/// </summary>
public static class ScenarioCatalog
{
    public static IReadOnlyList<ScenarioDef> All { get; } =
    [
        .. ProtocolScenarios.All,
        .. PatientScenarios.All,
        .. ClinicalScenarios.All,
        .. BoardingScenarios.All,
        .. InventoryScenarios.All,
        .. PurchasingScenarios.All,
        .. SalesScenarios.All,
        .. FinanceScenarios.All,
    ];
}
