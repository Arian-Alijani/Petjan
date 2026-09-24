using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.Sqlite.Configurations.Local;

// Installation-level rows: they describe the store/device itself, not tenant business data, so they are
// deliberately NOT tenant-filtered (the Tenant filter is applied only to SyncableEntity; these are [LocalOnly]).
// LocalInstallation is the source of the tenant id, so filtering it by tenant would be circular.

public sealed class LocalInstallationConfiguration : IEntityTypeConfiguration<LocalInstallation>
{
    public void Configure(EntityTypeBuilder<LocalInstallation> builder)
    {
        builder.HasKey(e => e.InstallationId);
    }
}

public sealed class ClockHighWaterConfiguration : IEntityTypeConfiguration<ClockHighWater>
{
    public void Configure(EntityTypeBuilder<ClockHighWater> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
    }
}

public sealed class StoreMaintenanceLogConfiguration : IEntityTypeConfiguration<StoreMaintenanceLog>
{
    public void Configure(EntityTypeBuilder<StoreMaintenanceLog> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => e.OccurredAtUtc).HasDatabaseName("IX_StoreMaintenanceLogs_OccurredAtUtc");
    }
}
