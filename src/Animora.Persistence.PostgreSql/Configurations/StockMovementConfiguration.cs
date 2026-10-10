using Animora.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Animora.Persistence.PostgreSql.Conventions;

namespace Animora.Persistence.PostgreSql.Configurations;

public sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("StockMovements", "public");
        OwnedRows.Money(builder, e => e.UnitCost);
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.TenantId, e.ProductId, e.VariantId, e.OccurredUtc }).HasDatabaseName("IX_StockMovements_TenantId_ProductId_VariantId_OccurredUtc");
        // Summed by stock projections: exact INTEGER fixed-scale (x1000), never SUM over decimal TEXT.
        OwnedRows.Quantity(builder, e => e.Quantity);
        builder.HasIndex(e => new { e.TenantId, e.Kind, e.OccurredUtc }).HasDatabaseName("IX_StockMovements_TenantId_Kind_OccurredUtc");
    }
}
