using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.Sqlite.Configurations.Identity;

// [LocalOnly] installation-level row: not tenant-filtered; every query binds tenant/user/device explicitly.
public sealed class DeviceCapabilitySnapshotConfiguration : IEntityTypeConfiguration<DeviceCapabilitySnapshot>
{
    public void Configure(EntityTypeBuilder<DeviceCapabilitySnapshot> builder)
    {
        builder.HasKey(e => e.Id); // Id columns and UTC-tick timestamps come from ModelConventions.

        builder.Property(e => e.PermissionsJson).IsRequired();
        builder.Property(e => e.PerCapabilityExpiryJson).IsRequired();
        builder.Property(e => e.SnapshotSignature).IsRequired();

        // One current snapshot per (tenant, user, device).
        builder.HasIndex(e => new { e.TenantId, e.UserId, e.DeviceId })
            .HasDatabaseName("IX_DeviceCapabilitySnapshots_Tenant_User_Device")
            .IsUnique();
    }
}
