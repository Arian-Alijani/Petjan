using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.Sqlite.Configurations.Notifications;

/// <summary>
/// Local notifications feed: the (tenant, published) index serves the feed/unread queries, the (tenant, scheduled)
/// index serves the "publish what is due" sweep, and the unique (tenant, dedup key) index makes automatic notices idempotent (SQLite treats NULL keys as distinct).
/// Column lengths come from <c>ModelConventions</c> (run after this); the long <c>Body</c> bound is set in the context.
/// </summary>
public sealed class NotificationConfiguration : IEntityTypeConfiguration<NotificationRow>
{
    public void Configure(EntityTypeBuilder<NotificationRow> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.TenantId, e.PublishedAtUtc })
            .HasDatabaseName("IX_Notifications_TenantId_PublishedAtUtc");
        builder.HasIndex(e => new { e.TenantId, e.ScheduledForUtc })
            .HasDatabaseName("IX_Notifications_TenantId_ScheduledForUtc");
        builder.HasIndex(e => new { e.TenantId, e.DedupKey }).IsUnique()
            .HasDatabaseName("IX_Notifications_TenantId_DedupKey");
    }
}

/// <summary>Read receipts keyed by (tenant, user, notification).</summary>
public sealed class NotificationReceiptConfiguration : IEntityTypeConfiguration<NotificationReceiptRow>
{
    public void Configure(EntityTypeBuilder<NotificationReceiptRow> builder)
    {
        builder.HasKey(e => new { e.TenantId, e.UserId, e.NotificationId });
    }
}

/// <summary>Reward grants: newest-first lookup per tenant.</summary>
public sealed class RewardGrantConfiguration : IEntityTypeConfiguration<RewardGrant>
{
    public void Configure(EntityTypeBuilder<RewardGrant> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.TenantId, e.TargetUserId })
            .HasDatabaseName("IX_RewardGrants_TenantId_TargetUserId");
    }
}

/// <summary>Vendor-message pull cursor: one row per tenant.</summary>
public sealed class NotificationFeedCursorConfiguration : IEntityTypeConfiguration<NotificationFeedCursor>
{
    public void Configure(EntityTypeBuilder<NotificationFeedCursor> builder) => builder.HasKey(e => e.TenantId);
}
