using Animora.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.Sqlite.Configurations.Scheduling;

public sealed class ReceptionQueueEntryConfiguration : IEntityTypeConfiguration<ReceptionQueueEntry>
{
    public void Configure(EntityTypeBuilder<ReceptionQueueEntry> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.TenantId, e.State, e.EnqueuedUtc }).HasDatabaseName("IX_ReceptionQueueEntries_TenantId_State_EnqueuedUtc");
    }
}
