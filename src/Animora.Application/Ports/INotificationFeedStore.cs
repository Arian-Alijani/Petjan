using Animora.Contracts.Wire.Sync;

namespace Animora.Application.Ports;

/// <summary>
/// Server side of vendor-message delivery (<c>GET /notifications</c>, <c>POST /notifications/receipts</c>): serves a
/// tenant's vendor-authored messages to a desktop device and records the device's real delivery acknowledgements.
/// Tenant-scoped by the caller's bound tenant; the feed is additionally narrowed to the audience of the requesting
/// user so one staff member's directed message is never handed to another user's device.
/// </summary>
public interface INotificationFeedStore
{
    /// <summary>
    /// The unexpired messages for the current tenant, addressed to <paramref name="userId"/> / <paramref name="roleName"/>,
    /// created after <paramref name="sinceTicks"/> (UTC ticks, 0 for the first pull), oldest first, at most <paramref name="take"/>.
    /// Scheduled-for-later messages are included (they carry <c>ScheduledForUtc</c>; the client publishes them when due)
    /// so a cursor that moved past their creation time can never skip them.
    /// </summary>
    Task<NotificationFeedPage> GetFeedAsync(long sinceTicks, int take, string userId, string roleName, CancellationToken ct);

    /// <summary>
    /// Records that <paramref name="deviceId"/> stored the given messages. Unknown / foreign-tenant ids are ignored and
    /// repeated reports are a no-op (unique per message+device). Returns the number of NEW delivery rows written.
    /// </summary>
    Task<int> RecordReceiptsAsync(string deviceId, string userId, IReadOnlyCollection<string> notificationIds, CancellationToken ct);
}
