using Animora.Domain.Patients;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.Sqlite.Configurations.Patients;

public sealed class OwnerConfiguration : IEntityTypeConfiguration<Owner>
{
    public void Configure(EntityTypeBuilder<Owner> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.TenantId, e.PhonePrimary }).HasDatabaseName("IX_Owners_TenantId_Phone");
        builder.HasIndex(e => new { e.TenantId, e.FullName }).HasDatabaseName("IX_Owners_TenantId_FullName");
    }
}
