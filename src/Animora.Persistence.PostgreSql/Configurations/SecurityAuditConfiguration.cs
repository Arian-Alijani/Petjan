using Animora.Persistence.PostgreSql.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.PostgreSql.Configurations;

/// <summary>
/// Maps <see cref="SecurityAuditEntry"/> to <c>public."SecurityAudit"</c> (S04). Append-only forever:
/// a database trigger (added by the <c>102_Identity</c> migration) rejects UPDATE/DELETE. Not a
/// <c>SyncableEntity</c>; the tenant query filter is applied by <c>AnimoraServerDbContext</c>.
/// </summary>
public sealed class SecurityAuditConfiguration : IEntityTypeConfiguration<SecurityAuditEntry>
{
    private const string Id26 = "character varying(26)";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SecurityAuditEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("SecurityAudit", "public");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasColumnType(Id26).HasMaxLength(26);
        builder.Property(e => e.TenantId).IsRequired().HasColumnType(Id26).HasMaxLength(26);
        builder.Property(e => e.ActorUserId).HasColumnType(Id26).HasMaxLength(26);
        builder.Property(e => e.DeviceId).HasColumnType(Id26).HasMaxLength(26);
        builder.Property(e => e.OccurredUtc).HasColumnType("timestamp with time zone").HasPrecision(6);
        builder.Property(e => e.Category).IsRequired().HasMaxLength(64);
        builder.Property(e => e.Action).IsRequired().HasMaxLength(64);
        builder.Property(e => e.Outcome).IsRequired().HasMaxLength(64);
        builder.Property(e => e.CorrelationId).IsRequired().HasMaxLength(64);
        builder.Property(e => e.DetailJson).HasColumnType("jsonb");

        builder.HasIndex(e => new { e.TenantId, e.OccurredUtc }).HasDatabaseName("IX_SecurityAudit_TenantId_OccurredUtc");
    }
}
