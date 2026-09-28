using Animora.Domain.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.Sqlite.Configurations.Sales;

public sealed class InsurerConfiguration : IEntityTypeConfiguration<Insurer>
{
    public void Configure(EntityTypeBuilder<Insurer> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.TenantId, e.Code })
            .HasDatabaseName("IX_Insurers_TenantId_Code")
            .IsUnique();
        builder.Property(e => e.CoveragePercent).HasPrecision(5, 2);
    }
}
