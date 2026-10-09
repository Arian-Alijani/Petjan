using Animora.Domain.Identity;
using Animora.Domain.Primitives;
using Animora.Persistence.PostgreSql.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Animora.Server.Tests.Persistence;

public sealed class ServerModelTests
{
    private static IEnumerable<IEntityType> Syncable(IModel model) =>
        model.GetEntityTypes().Where(t => typeof(SyncableEntity).IsAssignableFrom(t.ClrType));

    [Fact]
    public void EveryTenantIndex_StartsWithTenantId()
    {
        using var db = TestSupport.CreateContext();
        var types = Syncable(db.Model).ToArray();
        Assert.NotEmpty(types);

        foreach (var type in types)
        {
            foreach (var index in type.GetIndexes())
            {
                Assert.Equal(nameof(SyncableEntity.TenantId), index.Properties[0].Name);
            }
        }

        var tenantIndex = Assert.Single(db.Model.FindEntityType(typeof(Tenant))!.GetIndexes());
        Assert.Equal("IX_Tenants_TenantId_Status", tenantIndex.GetDatabaseName());
    }

    [Fact]
    public void EverySyncableEntity_HasNamedTenantAndSoftDeleteFilters()
    {
        using var db = TestSupport.CreateContext();

        foreach (var type in Syncable(db.Model))
        {
            var names = type.GetDeclaredQueryFilters().Select(f => f.Key).ToHashSet();
            Assert.Contains(ServerModelConventions.TenantFilterName, names);
            Assert.Contains(ServerModelConventions.SoftDeleteFilterName, names);
        }
    }

    [Fact]
    public void VersionIsConcurrencyToken_NotRowVersion()
    {
        using var db = TestSupport.CreateContext();

        foreach (var type in Syncable(db.Model))
        {
            var version = type.FindProperty(nameof(SyncableEntity.Version))!;
            Assert.True(version.IsConcurrencyToken);
            Assert.False(version.IsNullable);
            Assert.Equal("uuid", version.GetColumnType());
            Assert.Equal(ValueGenerated.OnAddOrUpdate, version.ValueGenerated);
            Assert.NotEqual("xmin", version.GetColumnName());
        }
    }

    [Fact]
    public void Tenant_ColumnShape_MatchesConventions()
    {
        using var db = TestSupport.CreateContext();
        var tenant = db.Model.FindEntityType(typeof(Tenant))!;

        Assert.Equal("Tenants", tenant.GetTableName());
        Assert.Equal("character varying(26)", tenant.FindProperty(nameof(Tenant.Id))!.GetColumnType());
        Assert.Equal("integer", tenant.FindProperty(nameof(Tenant.Status))!.GetColumnType());
        Assert.Equal(6, tenant.FindProperty(nameof(Tenant.UpdatedAt))!.GetPrecision());
        Assert.Null(tenant.FindProperty("DomainEvents"));
    }
}
