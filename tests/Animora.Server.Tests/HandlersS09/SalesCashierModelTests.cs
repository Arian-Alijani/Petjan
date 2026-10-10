using Animora.Domain.Sales;
using Animora.Domain.Cashier;
using Animora.Persistence.PostgreSql.Context;
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

namespace Animora.Server.Tests.HandlersS09;

public sealed class SalesCashierModelTests
{
    public static TheoryData<Type, string> Tables => new()
    {
        { typeof(Invoice), "Invoices" }, { typeof(Payment), "Payments" },
        { typeof(Insurer), "Insurers" }, { typeof(CashSession), "CashSessions" }
    };

    [Theory, MemberData(nameof(Tables))]
    public void SalesCashierTable_HasTenantIndexesFiltersAndUuid(Type clrType, string table)
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
    public void DocumentCounters_KeyIsTenantKindPeriod()
    {
        using var db = TestSupport.CreateContext();
        var entity = db.Model.FindEntityType(typeof(DocumentCounter))!;
        Assert.Equal(new[] { "TenantId", "Kind", "Period" }, entity.FindPrimaryKey()!.Properties.Select(p => p.Name));
        Assert.Equal("bigint", entity.FindProperty("Next")!.GetColumnType());
        Assert.Contains(entity.GetDeclaredQueryFilters(), f => f.Key == ServerModelConventions.TenantFilterName);
    }

    [Theory]
    [InlineData(typeof(InvoiceLine), "InvoiceLines", "LineId")]
    [InlineData(typeof(InvoiceAdjustment), "InvoiceAdjustments", "AdjustmentId")]
    [InlineData(typeof(CashMovement), "CashMovements", "MovementId")]
    public void ChildRows_HaveCompositeCascadeKeys(Type type, string table, string key)
    {
        using var db = TestSupport.CreateContext();
        var row = db.Model.FindEntityType(type)!;
        Assert.Equal(table, row.GetTableName());
        Assert.Equal(new[] { "OwnerId", key }, row.FindPrimaryKey()!.Properties.Select(p => p.Name));
        Assert.Equal(DeleteBehavior.Cascade, Assert.Single(row.GetForeignKeys()).DeleteBehavior);
    }
}
