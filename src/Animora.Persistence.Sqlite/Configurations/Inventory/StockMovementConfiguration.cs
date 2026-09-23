using Animora.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Animora.Persistence.Sqlite.Conventions;

namespace Animora.Persistence.Sqlite.Configurations.Inventory;

public sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.TenantId, e.ProductId, e.VariantId, e.OccurredUtc }).HasDatabaseName("IX_StockMovements_TenantId_ProductId_VariantId_OccurredUtc");
        builder.HasIndex(e => new { e.TenantId, e.Kind, e.OccurredUtc }).HasDatabaseName("IX_StockMovements_TenantId_Kind_OccurredUtc");
    }
}
