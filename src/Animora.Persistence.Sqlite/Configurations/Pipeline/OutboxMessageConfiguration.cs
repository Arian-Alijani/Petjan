using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.Sqlite.Configurations.Pipeline;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    /// <summary>Payload columns hold arbitrary event JSON, far larger than the generic short-text default.</summary>
    private const int PayloadMaxLength = 65536;

    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.PayloadJson).HasColumnType("TEXT").HasMaxLength(PayloadMaxLength);

        builder.Property(e => e.ActorUserId).HasMaxLength(26);

        builder.HasIndex(e => new { e.Status, e.NextAttemptUtc })
            .HasDatabaseName("IX_OutboxMessages_Status_NextAttemptUtc");
        builder.HasIndex(e => e.MessageType).HasDatabaseName("IX_OutboxMessages_MessageType");
    }
}
