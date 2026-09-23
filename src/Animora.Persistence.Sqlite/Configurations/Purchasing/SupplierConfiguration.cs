using Animora.Domain.Purchasing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Animora.Persistence.Sqlite.Conventions;

namespace Animora.Persistence.Sqlite.Configurations.Purchasing;

public sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.TenantId, e.Name }).HasDatabaseName("IX_Suppliers_TenantId_Name");
    }
}
