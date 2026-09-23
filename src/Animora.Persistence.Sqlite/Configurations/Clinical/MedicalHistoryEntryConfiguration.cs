using Animora.Domain.Clinical;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.Sqlite.Configurations.Clinical;

public sealed class MedicalHistoryEntryConfiguration : IEntityTypeConfiguration<MedicalHistoryEntry>
{
    public void Configure(EntityTypeBuilder<MedicalHistoryEntry> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.TenantId, e.PetId, e.OccurredUtc }).HasDatabaseName("IX_MedicalHistoryEntries_TenantId_PetId_OccurredUtc");
    }
}
