using Animora.Domain.Primitives;
using Animora.Persistence.PostgreSql.Replication;
using Animora.Server.Tests.Persistence;
using Xunit;
namespace Animora.Server.Tests.Replication;

public sealed class CatalogTests
{
    [Fact]
    public void EverySyncableEntity_IsReplicatedOrExcluded()
    {
        using var db = TestSupport.CreateContext();
        var catalog = SyncContractCatalog.ProjectionOrder.Select(x => x.Entity).ToHashSet();
        foreach (var entity in db.Model.GetEntityTypes().Where(x => typeof(SyncableEntity).IsAssignableFrom(x.ClrType)))
            Assert.True(catalog.Contains(entity.ClrType) || SyncExclusions.Reasons.ContainsKey(entity.ClrType.Name), entity.ClrType.Name);
    }
    [Fact]
    public void ProjectionOrder_ParentsBeforeChildren()
    {
        var names = SyncContractCatalog.ProjectionOrder.Select(x => x.Name).ToList();
        foreach (var pair in new[] { ("Owner", "Pet"), ("Pet", "Visit"), ("ProductCategory", "Product"), ("TreatmentPlan", "TreatmentOccurrence"), ("Room", "BoardingStay") })
            Assert.True(names.IndexOf(pair.Item1) < names.IndexOf(pair.Item2));
        Assert.Equal(names.Count, names.Distinct().Count());
    }
    [Fact]
    public void NoMagicTotals()
    {
        var dir = Path.Combine(RepoRoot.Find(), "src/Animora.Persistence.PostgreSql/Replication");
        foreach (var file in Directory.EnumerateFiles(dir, "*.cs"))
            Assert.DoesNotMatch(@"\b\d+\s+entities\b", File.ReadAllText(file));
    }
    [Fact]
    public void PostC11_TreatmentPlanFamily_Replicated_RecurringExpense_Excluded()
    {
        foreach (var name in new[] { "TreatmentPlan", "TreatmentOccurrence", "VaccineCatalogItem" })
            Assert.Contains(SyncContractCatalog.ProjectionOrder, x => x.Name == name);
        Assert.True(SyncExclusions.Reasons.ContainsKey("RecurringExpense"));
    }
}
