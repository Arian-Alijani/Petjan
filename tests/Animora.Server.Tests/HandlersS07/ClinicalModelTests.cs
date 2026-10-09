using Animora.Domain.Patients;
using Animora.Domain.Clinical;
using Animora.Domain.Scheduling;
using Animora.Domain.Primitives;
using Animora.Persistence.PostgreSql.Clinical;
using Animora.Persistence.PostgreSql.Conventions;
using Animora.Server.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Animora.Server.Tests.HandlersS07;

public sealed class ClinicalModelTests
{
    public static TheoryData<Type, string> Tables => new()
    {
        { typeof(Owner), "Owners" }, { typeof(Pet), "Pets" },
        { typeof(WeightEntry), "WeightEntries" }, { typeof(DocumentReference), "DocumentReferences" },
        { typeof(MedicalHistoryEntry), "MedicalHistory" }, { typeof(ReceptionQueueEntry), "ReceptionQueueEntries" },
        { typeof(Visit), "Visits" }, { typeof(VaccinationRecord), "VaccinationRecords" },
        { typeof(TreatmentPlan), "TreatmentPlans" }, { typeof(TreatmentOccurrence), "TreatmentOccurrences" },
        { typeof(VaccineCatalogItem), "VaccineCatalogItems" }, { typeof(VisitBillingHandoff), "VisitHandoffLines" }
    };

    [Theory, MemberData(nameof(Tables))]
    public void ClinicalTable_HasTenantIndexesFiltersAndUuid(Type clrType, string table)
    {
        using var db = TestSupport.CreateContext();
        var entity = db.Model.FindEntityType(clrType);
        Assert.NotNull(entity);
        Assert.Equal(table, entity.GetTableName());
        Assert.Equal("public", entity.GetSchema());
        Assert.NotEmpty(entity.GetIndexes());
        Assert.All(entity.GetIndexes(), index => Assert.Equal("TenantId", index.Properties[0].Name));
        var names = entity.GetDeclaredQueryFilters().Select(f => f.Key).ToArray();
        Assert.Contains(ServerModelConventions.TenantFilterName, names);
        Assert.Contains(ServerModelConventions.SoftDeleteFilterName, names);
        var version = entity.FindProperty(nameof(SyncableEntity.Version));
        Assert.NotNull(version);
        Assert.Equal("uuid", version.GetColumnType());
        Assert.True(version.IsConcurrencyToken);
    }

    [Fact]
    public void PrescriptionRows_AreCompositeCascadeChildren()
    {
        using var db = TestSupport.CreateContext();
        var row = db.Model.FindEntityType(typeof(PrescriptionLine));
        Assert.NotNull(row);
        Assert.Equal("VisitRxLines", row.GetTableName());
        Assert.Equal(new[] { "OwnerId", "LineId" }, row.FindPrimaryKey()!.Properties.Select(p => p.Name));
        Assert.Equal(DeleteBehavior.Cascade, Assert.Single(row.GetForeignKeys()).DeleteBehavior);
    }
}

public sealed class ReplicatedSetTests
{
    [Fact]
    public void TreatmentPlan_AndVaccineCatalog_AreMapped_WithTenantFilter()
    {
        using var db = TestSupport.CreateContext();
        foreach (var type in new[] { typeof(TreatmentPlan), typeof(TreatmentOccurrence), typeof(VaccineCatalogItem) })
        {
            var entity = db.Model.FindEntityType(type);
            Assert.NotNull(entity);
            Assert.Contains(entity.GetDeclaredQueryFilters(), f => f.Key == "Tenant");
        }
    }
}
