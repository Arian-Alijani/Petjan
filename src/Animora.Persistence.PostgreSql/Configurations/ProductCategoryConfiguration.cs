using Animora.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Animora.Persistence.PostgreSql.Conventions;

namespace Animora.Persistence.PostgreSql.Configurations;

public sealed class ProductCategoryConfiguration : IEntityTypeConfiguration<ProductCategory>
{
    public void Configure(EntityTypeBuilder<ProductCategory> builder)
    {
        builder.ToTable("ProductCategories", "public");
        builder.HasIndex(e => new { e.TenantId, e.Code }).IsUnique().HasDatabaseName("IX_ProductCategories_TenantId_Code");
        builder.HasKey(e => e.Id);
    }
}
