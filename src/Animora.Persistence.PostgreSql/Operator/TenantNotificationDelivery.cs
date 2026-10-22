using Animora.Domain.Primitives;

namespace Animora.Persistence.PostgreSql.Operator;

/// <summary>
/// The real delivery acknowledgement of one <see cref="TenantNotification"/> by one desktop device: written when
/// the client reports (<c>POST /notifications/receipts</c>) that it has materialised the message into its local
/// store. Unique per <c>(NotificationId, DeviceId)</c> so a repeated report is an idempotent no-op. The admin
/// panel counts these rows against the tenant's active devices to show how many clients truly received a message.
/// </summary>
public sealed class TenantNotificationDelivery
{
    public string Id { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string NotificationId { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public DateTimeOffset ReceivedUtc { get; set; }

    public static TenantNotificationDelivery Create(DateTimeOffset now, string tenantId, string notificationId,
        string deviceId, string userId) => new()
        {
            Id = IdGenerator.NewId(now), TenantId = tenantId, NotificationId = notificationId, DeviceId = deviceId,
            UserId = userId, ReceivedUtc = now.ToUniversalTime(),
        };
}
