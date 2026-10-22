using Animora.Persistence.PostgreSql.Operator;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.PostgreSql.Configurations;

public sealed class TenantNotificationDeliveryConfiguration : IEntityTypeConfiguration<TenantNotificationDelivery>
{
    public void Configure(EntityTypeBuilder<TenantNotificationDelivery> b)
    {
        b.ToTable("TenantNotificationDeliveries", "public"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(26); b.Property(x => x.TenantId).HasMaxLength(26).IsRequired();
        b.Property(x => x.NotificationId).HasMaxLength(26).IsRequired();
        b.Property(x => x.DeviceId).HasMaxLength(26).IsRequired();
        b.Property(x => x.UserId).HasMaxLength(26).IsRequired();
        b.Property(x => x.ReceivedUtc).HasColumnType("timestamp(6) with time zone");
        b.HasIndex(x => new { x.NotificationId, x.DeviceId }).IsUnique()
            .HasDatabaseName("UX_TenantNotificationDeliveries_NotificationId_DeviceId");
        b.HasIndex(x => new { x.TenantId, x.NotificationId })
            .HasDatabaseName("IX_TenantNotificationDeliveries_TenantId_NotificationId");
    }
}
