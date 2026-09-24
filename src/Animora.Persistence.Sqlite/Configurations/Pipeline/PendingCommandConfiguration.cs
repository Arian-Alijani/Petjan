using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.Sqlite.Configurations.Pipeline;

public sealed class PendingCommandConfiguration : IEntityTypeConfiguration<PendingCommand>
{
    /// <summary>Payload columns hold arbitrary command JSON, far larger than the generic short-text default.</summary>
    private const int PayloadMaxLength = 65536;

    public void Configure(EntityTypeBuilder<PendingCommand> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.PayloadJson).HasColumnType("TEXT").HasMaxLength(PayloadMaxLength);
        builder.Property(e => e.CommandFingerprint)
            .HasColumnType("TEXT").HasMaxLength(PendingCommand.FingerprintLength).IsRequired();

        builder.HasIndex(e => e.OperationId).IsUnique().HasDatabaseName("IX_PendingCommands_OperationId");
        builder.HasIndex(e => e.LocalSequence).IsUnique().HasDatabaseName("IX_PendingCommands_LocalSequence");
        builder.HasIndex(e => new { e.Status, e.NextAttemptUtc })
            .HasDatabaseName("IX_PendingCommands_Status_NextAttemptUtc");
        builder.HasIndex(e => new { e.AggregateType, e.AggregateId })
            .HasDatabaseName("IX_PendingCommands_AggregateType_AggregateId");
        builder.HasIndex(e => e.DependsOnOperationId)
            .HasDatabaseName("IX_PendingCommands_DependsOnOperationId");
    }
}
