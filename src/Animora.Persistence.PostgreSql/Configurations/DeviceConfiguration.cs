using Animora.Domain.Identity;
using Animora.Persistence.PostgreSql.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.PostgreSql.Configurations;

/// <summary>Maps <see cref="Device"/> to <c>public."Devices"</c> (S04): one row per user installation.</summary>
public sealed class DeviceConfiguration : IEntityTypeConfiguration<Device>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Device> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Devices", "public");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.UserId).IsRequired().HasColumnType("character varying(26)").HasMaxLength(26);
        builder.Property(e => e.DeviceName).IsRequired().HasMaxLength(128);
        builder.Property(e => e.InstallationIdHash).IsRequired().HasMaxLength(128);
        builder.Property(e => e.Platform).IsRequired().HasMaxLength(64);
        builder.Property(e => e.Status).HasConversion<int>().HasColumnType("integer");
        builder.Property(e => e.RegisteredUtc).HasColumnType("timestamp with time zone").HasPrecision(6);
        builder.Property(e => e.LastContactUtc).HasColumnType("timestamp with time zone").HasPrecision(6);
        builder.Property(e => e.RevokedUtc).HasColumnType("timestamp with time zone").HasPrecision(6);
        builder.Property(e => e.RevocationReason).HasMaxLength(256);

        // IsUsable is derived from Status, never stored.
        builder.Ignore(e => e.IsUsable);

        ServerModelConventions.TenantIndex(builder, nameof(Device.InstallationIdHash), nameof(Device.UserId)).IsUnique();
    }
}
