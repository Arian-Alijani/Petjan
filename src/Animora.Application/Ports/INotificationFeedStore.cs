using Animora.Contracts.Wire.Sync;

namespace Animora.Application.Ports;

/// <summary>
/// Server side of vendor-message delivery (<c>GET /notifications</c>, <c>POST /notifications/receipts</c>): serves a
/// tenant's vendor-authored messages to a desktop device and records the device's real delivery acknowledgements.
/// Tenant-scoped by the caller's bound tenant. The audience (all / role / one user) is NOT filtered here: the desktop
/// applies it per signed-in user when reading its local store (<c>NotificationService.VisibleTo</c>), which keeps the
/// pull cursor per tenant/device and means a message is "delivered" to a device once, whoever signs in on it.
/// </summary>
public interface INotificationFeedStore
{
    /// <summary>
    /// The unexpired messages for the current tenant created after <paramref name="sinceTicks"/> (UTC ticks, 0 for the first pull), oldest first, at most <paramref name="take"/>.
    /// Scheduled-for-later messages are included (they carry <c>ScheduledForUtc</c>; the client publishes them when due)
    /// so a cursor that moved past their creation time can never skip them.
    /// </summary>
    Task<NotificationFeedPage> GetFeedAsync(long sinceTicks, int take, CancellationToken ct);

    /// <summary>
    /// Records that <paramref name="deviceId"/> stored the given messages. Unknown / foreign-tenant ids are ignored and
    /// repeated reports are a no-op (unique per message+device). Returns the number of NEW delivery rows written.
    /// </summary>
    Task<int> RecordReceiptsAsync(string deviceId, string userId, IReadOnlyCollection<string> notificationIds, CancellationToken ct);
}
