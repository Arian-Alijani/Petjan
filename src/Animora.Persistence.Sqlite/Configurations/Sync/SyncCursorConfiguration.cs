using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.Sqlite.Configurations.Sync;

/// <summary>
/// Maps <see cref="SyncCursor"/>: ULID key, a unique (tenant, scope) index so there is exactly one
/// cursor row per tenant + sync scope. Auto-discovered by
/// <c>ApplyConfigurationsFromAssembly</c>; the <c>[LocalOnly]</c> tag is applied by
/// <c>ModelConventions</c>.
/// </summary>
public sealed class SyncCursorConfiguration : IEntityTypeConfiguration<SyncCursor>
{
    public void Configure(EntityTypeBuilder<SyncCursor> builder)
    {
        // Scalar lengths/types are owned by ModelConventions (bounded TEXT, UTC-ticks for
        // DateTimeOffset); this configuration only declares the key and the uniqueness rule.
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.TenantId, e.Scope })
            .IsUnique()
            .HasDatabaseName("IX_SyncCursors_TenantId_Scope");
    }
}
