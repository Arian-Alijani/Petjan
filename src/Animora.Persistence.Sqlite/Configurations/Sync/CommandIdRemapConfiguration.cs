using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.Sqlite.Configurations.Sync;

/// <summary>SYN-01: <c>CommandIdRemaps</c> keyed by (tenant, local id); the age index serves retention.</summary>
public sealed class CommandIdRemapConfiguration : IEntityTypeConfiguration<CommandIdRemap>
{
    public void Configure(EntityTypeBuilder<CommandIdRemap> builder)
    {
        builder.HasKey(e => new { e.TenantId, e.LocalId });
        builder.HasIndex(e => e.CreatedAtUtc).HasDatabaseName("IX_CommandIdRemaps_CreatedAtUtc");
    }
}
