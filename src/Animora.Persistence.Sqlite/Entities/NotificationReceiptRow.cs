using Animora.Domain.Primitives;

namespace Animora.Persistence.Sqlite.Entities;

/// <summary>
/// Per-user read state of one <see cref="NotificationRow"/>. A message with no receipt row for a user is unread for
/// that user. Keyed by (tenant, user, notification) so marking read twice is an idempotent overwrite.
/// <see cref="LocalOnlyAttribute">Local-only</see>, never replicated.
/// </summary>
[LocalOnly]
public sealed class NotificationReceiptRow
{
    private NotificationReceiptRow() { }

    /// <summary>Owning tenant (explicit predicate in every query).</summary>
    public string TenantId { get; private set; } = string.Empty;

    /// <summary>The reader.</summary>
    public string UserId { get; private set; } = string.Empty;

    /// <summary>The message that was read.</summary>
    public string NotificationId { get; private set; } = string.Empty;

    /// <summary>When it was read; null = seen but not acknowledged.</summary>
    public DateTimeOffset? ReadAtUtc { get; private set; }

    /// <summary>Creates a read receipt.</summary>
    public static NotificationReceiptRow Create(string tenantId, string userId, string notificationId, DateTimeOffset readAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(notificationId);
        return new() { TenantId = tenantId, UserId = userId, NotificationId = notificationId, ReadAtUtc = readAtUtc };
    }

    /// <summary>Marks the message read (the first read time wins).</summary>
    public void MarkRead(DateTimeOffset nowUtc) => ReadAtUtc ??= nowUtc;
}
