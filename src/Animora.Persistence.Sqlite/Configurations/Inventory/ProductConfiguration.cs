using Animora.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Animora.Persistence.Sqlite.Conventions;

namespace Animora.Persistence.Sqlite.Configurations.Inventory;

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.TenantId, e.Name }).HasDatabaseName("IX_Products_TenantId_Name");
        builder.HasIndex(e => new { e.TenantId, e.CategoryId }).HasDatabaseName("IX_Products_TenantId_CategoryId");
        builder.HasIndex(e => new { e.TenantId, e.Sku }).HasDatabaseName("IX_Products_TenantId_Sku").IsUnique();
        OwnedRows.Map(builder, e => e.Variants, "_variants", "ProductVariants", "VariantId");
    }
}
