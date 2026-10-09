using Animora.Domain.Clinical;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.PostgreSql.Configurations;

public sealed class MedicalHistoryEntryConfiguration : IEntityTypeConfiguration<MedicalHistoryEntry>
{
    public void Configure(EntityTypeBuilder<MedicalHistoryEntry> builder)
    {
        builder.ToTable("MedicalHistory", "public");
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.TenantId, e.PetId, e.OccurredUtc }).HasDatabaseName("IX_MedicalHistoryEntries_TenantId_PetId_OccurredUtc");
        // D23: "was this dose already recorded" probe of the doctor's reminder list.
        builder.HasIndex(e => new { e.TenantId, e.SourceEntityId }).HasDatabaseName("IX_MedicalHistoryEntries_TenantId_SourceEntityId");
    }
}
