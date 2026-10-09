using Animora.Persistence.PostgreSql.Commands;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.PostgreSql.Configurations;

/// <summary>Maps <see cref="CommandIdMapEntry"/> to <c>public."CommandIdMap"</c> (S06), PK <c>(TenantId, OperationId, Slot)</c>.</summary>
public sealed class CommandIdMapConfiguration : IEntityTypeConfiguration<CommandIdMapEntry>
{
    private const string Id26 = "character varying(26)";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<CommandIdMapEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("CommandIdMap", "public");
        builder.HasKey(e => new { e.TenantId, e.OperationId, e.Slot });

        builder.Property(e => e.TenantId).IsRequired().HasColumnType(Id26).HasMaxLength(26);
        builder.Property(e => e.OperationId).IsRequired().HasColumnType(Id26).HasMaxLength(26);
        builder.Property(e => e.Slot).HasColumnType("smallint");
        builder.Property(e => e.EntityType).IsRequired().HasMaxLength(32);
        builder.Property(e => e.ServerEntityId).IsRequired().HasColumnType(Id26).HasMaxLength(26);
    }
}
