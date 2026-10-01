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
        // D23: the appointment-to-queue-entry join of the dashboard queue count. Partial on purpose: walk-ins
        // (AppointmentId IS NULL) keep using the State/EnqueuedUtc range instead of scanning all walk-in history.
        builder.HasIndex(e => new { e.TenantId, e.AppointmentId }).HasDatabaseName("IX_ReceptionQueueEntries_TenantId_AppointmentId")
            .HasFilter("\"AppointmentId\" IS NOT NULL");
    }
}
