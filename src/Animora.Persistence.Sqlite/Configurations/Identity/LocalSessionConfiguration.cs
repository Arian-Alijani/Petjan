using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.Sqlite.Configurations.Identity;

// [LocalOnly] installation-level row: not tenant-filtered (see LocalHardeningConfigurations).
public sealed class LocalSessionConfiguration : IEntityTypeConfiguration<LocalSession>
{
    public void Configure(EntityTypeBuilder<LocalSession> builder)
    {
        builder.HasKey(e => e.Id); // column types/lengths come from ModelConventions.
        builder.Ignore(e => e.IsActive);

        // One active session per device (the local store has exactly one device).
        builder.HasIndex(e => e.DeviceId)
            .HasDatabaseName("IX_LocalSessions_DeviceId_Active")
            .IsUnique()
            .HasFilter("\"EndedAtUtc\" IS NULL");
    }
}
