using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.Sqlite.Configurations.Diagnostics;

public sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Action).HasMaxLength(AuditEntry.ActionMaxLength).IsRequired();
        builder.Property(e => e.EntityType).HasMaxLength(AuditEntry.EntityTypeMaxLength);
        builder.Property(e => e.BeforeRef).HasMaxLength(AuditEntry.ReferenceMaxLength);
        builder.Property(e => e.AfterRef).HasMaxLength(AuditEntry.ReferenceMaxLength);
        builder.Property(e => e.Detail).HasMaxLength(AuditEntry.DetailMaxLength);
        builder.HasIndex(e => e.OccurredAtUtc).HasDatabaseName("IX_AuditEntries_OccurredAtUtc");
        builder.HasIndex(e => new { e.Category, e.OccurredAtUtc })
            .HasDatabaseName("IX_AuditEntries_Category_OccurredAtUtc");
        builder.HasIndex(e => new { e.EntityType, e.EntityId })
            .HasDatabaseName("IX_AuditEntries_EntityType_EntityId");
        builder.HasIndex(e => e.OperationId).HasDatabaseName("IX_AuditEntries_OperationId");
    }
}
