using Animora.Persistence.PostgreSql.Operator;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.PostgreSql.Configurations;

public sealed class TenantNotificationConfiguration : IEntityTypeConfiguration<TenantNotification>
{
    public void Configure(EntityTypeBuilder<TenantNotification> b)
    {
        b.ToTable("TenantNotifications", "public"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(26); b.Property(x => x.TenantId).HasMaxLength(26).IsRequired();
        b.Property(x => x.AudienceKind).HasColumnType("smallint"); b.Property(x => x.AudienceValue).HasMaxLength(64);
        b.Property(x => x.Kind).HasColumnType("smallint"); b.Property(x => x.Tag).HasMaxLength(TenantNotification.TagMaxLength);
        b.Property(x => x.Title).HasMaxLength(TenantNotification.TitleMaxLength).IsRequired();
        b.Property(x => x.Body).HasMaxLength(TenantNotification.BodyMaxLength).IsRequired();
        b.Property(x => x.CreatedByOperatorId).HasMaxLength(26).IsRequired();
        b.Property(x => x.CreatedAtUtc).HasColumnType("timestamp(6) with time zone");
        b.Property(x => x.ScheduledForUtc).HasColumnType("timestamp(6) with time zone");
        b.Property(x => x.ExpiresAtUtc).HasColumnType("timestamp(6) with time zone");
        b.HasIndex(x => new { x.TenantId, x.CreatedAtUtc }).HasDatabaseName("IX_TenantNotifications_TenantId_CreatedAtUtc");
    }
}
