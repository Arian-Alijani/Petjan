using Animora.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Animora.Persistence.PostgreSql.Conventions;

namespace Animora.Persistence.PostgreSql.Configurations;

public sealed class UnitOfMeasureConfiguration : IEntityTypeConfiguration<UnitOfMeasure>
{
    public void Configure(EntityTypeBuilder<UnitOfMeasure> builder)
    {
        builder.ToTable("UnitsOfMeasure", "public");
        builder.HasIndex(e => new { e.TenantId, e.Code }).IsUnique().HasDatabaseName("IX_UnitsOfMeasure_TenantId_Code");
        builder.HasKey(e => e.Id);
    }
}
