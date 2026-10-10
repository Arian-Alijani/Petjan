using Animora.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Animora.Persistence.PostgreSql.Conventions;

namespace Animora.Persistence.PostgreSql.Configurations;

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products", "public");
        OwnedRows.Quantity(builder, e => e.ReorderLevel);
        OwnedRows.Money(builder, e => e.DefaultCostPrice);
        OwnedRows.Money(builder, e => e.DefaultSalePrice);
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.TenantId, e.Name }).HasDatabaseName("IX_Products_TenantId_Name");
        builder.HasIndex(e => new { e.TenantId, e.CategoryId }).HasDatabaseName("IX_Products_TenantId_CategoryId");
        builder.HasIndex(e => new { e.TenantId, e.Sku }).HasDatabaseName("IX_Products_TenantId_Sku").IsUnique().HasFilter("\"Sku\" IS NOT NULL");
        OwnedRows.Map(builder, e => e.Variants);
    }
}

/// <summary>Child rows of <see cref="ProductVariant"/>'s aggregate, table <c>ProductVariants</c>.</summary>
public sealed class ProductVariantConfiguration : IEntityTypeConfiguration<ProductVariant>
{
    public void Configure(EntityTypeBuilder<ProductVariant> builder)
    {
        OwnedRows.Row(builder, "ProductVariants", e => e.VariantId);
        OwnedRows.Money(builder, e => e.CostPrice);
        OwnedRows.Money(builder, e => e.SalePrice);
        // Quantities are persisted exactly as bigint x1000, not decimal text.
        OwnedRows.Quantity(builder, e => e.ReorderLevel);
    }
}
