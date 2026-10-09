using Animora.Domain.Boarding;
using Animora.Persistence.PostgreSql.Boarding;
using Animora.Persistence.PostgreSql.Scheduling;
using Animora.Domain.Patients;
using Animora.Domain.Clinical;
using Animora.Domain.Scheduling;
using Animora.Domain.Primitives;
using Animora.Persistence.PostgreSql.Clinical;
using Animora.Persistence.PostgreSql.Conventions;
using Animora.Server.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Animora.Server.Tests.HandlersS08;

public sealed class SchedulingBoardingModelTests
{
    public static TheoryData<Type, string> Tables => new()
    {
        { typeof(Appointment), "Appointments" }, { typeof(AppointmentSettingsEntity), "AppointmentSettings" },
        { typeof(Room), "Rooms" }, { typeof(BoardingServiceDef), "BoardingServiceDefs" },
        { typeof(BoardingStay), "BoardingStays" }, { typeof(BoardingBillingHandoff), "BoardingHandoffLines" }
    };

    [Theory, MemberData(nameof(Tables))]
    public void SchedulingBoardingTable_HasTenantIndexesFiltersAndUuid(Type clrType, string table)
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
    public void StayItems_AreCompositeCascadeChildren()
    {
        using var db = TestSupport.CreateContext();
        var row = db.Model.FindEntityType(typeof(StayItem));
        Assert.NotNull(row);
        Assert.Equal("BoardingStayItems", row.GetTableName());
        Assert.Equal(new[] { "OwnerId", "ItemId" }, row.FindPrimaryKey()!.Properties.Select(p => p.Name));
        Assert.Equal(DeleteBehavior.Cascade, Assert.Single(row.GetForeignKeys()).DeleteBehavior);
    }
}
