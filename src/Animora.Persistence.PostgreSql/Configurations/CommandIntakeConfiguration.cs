using Animora.Persistence.PostgreSql.Commands;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.PostgreSql.Configurations;

/// <summary>
/// Maps <see cref="CommandIntakeRecord"/> to <c>public."CommandIntake"</c> (S06) — the tenant-scoped idempotency
/// store, composite key <c>(TenantId, OperationId)</c>. Not a <c>SyncableEntity</c>; the <c>"Tenant"</c> filter is
/// applied by <c>AnimoraServerDbContext</c>.
/// </summary>
public sealed class CommandIntakeConfiguration : IEntityTypeConfiguration<CommandIntakeRecord>
{
    private const string Id26 = "character varying(26)";
    private const string Timestamptz = "timestamp with time zone";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<CommandIntakeRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("CommandIntake", "public");
        builder.HasKey(e => new { e.TenantId, e.OperationId });

        builder.Property(e => e.TenantId).IsRequired().HasColumnType(Id26).HasMaxLength(26);
        builder.Property(e => e.OperationId).IsRequired().HasColumnType(Id26).HasMaxLength(26);
        builder.Property(e => e.CommandType).IsRequired().HasMaxLength(64);
        builder.Property(e => e.Fingerprint).IsRequired().HasColumnType("character(64)").HasMaxLength(64).IsFixedLength();
        builder.Property(e => e.Status).HasConversion<short>().HasColumnType("smallint");
        builder.Property(e => e.Outcome).HasColumnType("smallint");
        builder.Property(e => e.ErrorCode).HasMaxLength(64);
        builder.Property(e => e.Detail).HasColumnType("text").HasMaxLength(512);
        builder.Property(e => e.ServerEntityId).HasColumnType(Id26).HasMaxLength(26);
        builder.Property(e => e.ServerVersion).HasColumnType("uuid");
        builder.Property(e => e.FinancialEffect).HasColumnType("smallint");
        builder.Property(e => e.ResponsibleRole).HasMaxLength(32);
        builder.Property(e => e.ReceivedUtc).HasColumnType(Timestamptz).HasPrecision(6);
        builder.Property(e => e.SettledUtc).HasColumnType(Timestamptz).HasPrecision(6);
        builder.Property(e => e.AttemptId).HasColumnType(Id26).HasMaxLength(26);
        builder.Property(e => e.LeaseOwner).HasMaxLength(64);
        builder.Property(e => e.LeaseUntilUtc).HasColumnType(Timestamptz).HasPrecision(6);
        builder.Property(e => e.AttemptCount).HasColumnType("smallint");
        builder.Property(e => e.PayloadJson).HasColumnType("jsonb");
        builder.Property(e => e.DependsOnOperationId).HasColumnType(Id26).HasMaxLength(26);
        builder.Property(e => e.CorrelationId).IsRequired().HasMaxLength(64);

        builder.HasIndex(e => new { e.TenantId, e.Status, e.LeaseUntilUtc })
            .HasDatabaseName("IX_CommandIntake_TenantId_Status_LeaseUntilUtc");
        builder.HasIndex(e => new { e.TenantId, e.DependsOnOperationId })
            .HasDatabaseName("IX_CommandIntake_TenantId_DependsOnOperationId");
    }
}
