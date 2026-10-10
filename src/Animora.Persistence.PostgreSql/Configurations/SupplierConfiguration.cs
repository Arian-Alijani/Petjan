using Animora.Domain.Purchasing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Animora.Persistence.PostgreSql.Conventions;

namespace Animora.Persistence.PostgreSql.Configurations;

public sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.ToTable("Suppliers", "public");
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.TenantId, e.Name }).HasDatabaseName("IX_Suppliers_TenantId_Name");
    }
}
