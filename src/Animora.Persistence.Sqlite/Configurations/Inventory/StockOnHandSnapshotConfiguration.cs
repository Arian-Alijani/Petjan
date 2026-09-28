using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.Sqlite.Configurations.Inventory;

/// <summary>C06 maintained on-hand projection: one row per (tenant, product, variant).</summary>
public sealed class StockOnHandSnapshotConfiguration : IEntityTypeConfiguration<StockOnHandSnapshot>
{
    public void Configure(EntityTypeBuilder<StockOnHandSnapshot> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.OnHandScaled).HasColumnType("INTEGER");
        builder.HasIndex(e => new { e.TenantId, e.ProductId, e.VariantId }).IsUnique()
            .HasDatabaseName("IX_StockOnHandSnapshots_TenantId_ProductId_VariantId");
    }
}
