using Animora.Domain.Patients;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.Sqlite.Configurations.Patients;

public sealed class PetConfiguration : IEntityTypeConfiguration<Pet>
{
    public void Configure(EntityTypeBuilder<Pet> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.TenantId, e.OwnerId }).HasDatabaseName("IX_Pets_TenantId_OwnerId");
        builder.HasIndex(e => new { e.TenantId, e.Name }).HasDatabaseName("IX_Pets_TenantId_Name");
    }
}
