namespace Animora.Contracts.Wire.Sync;

/// <summary>
/// Vendor-message delivery — a bounded, read-only snapshot of the vendor-authored messages addressed to a tenant
/// that were published at or after a timestamp cursor, served by <c>GET /notifications</c>. This is a SEPARATE
/// read-only channel from the command intake (<c>/api/commands</c>) and the generic state feed (<c>/sync/*</c>):
/// <c>TenantNotification</c> rows are vendor-global (authored in the operator/admin panel) and are intentionally
/// excluded from the journal-based feed, so this dedicated path is the only way a desktop client receives them.
/// Rows are server-authored and NEVER fed back into the command pipeline — the client upserts them into its local
/// <c>NotificationRow</c> store keyed by the stable id, then confirms real delivery with
/// <see cref="NotificationReceiptReport"/> (<c>POST /notifications/receipts</c>).
/// </summary>
/// <param name="Items">The published, unexpired messages for the tenant whose creation tick is &gt; the request cursor.</param>
/// <param name="NextCursorTicks">
/// The exclusive cursor to pass on the next poll: the maximum <c>CreatedAtUtc</c> tick across the returned rows
/// (or the request cursor when nothing new). UTC ticks; monotonic within a tenant.
/// </param>
/// <param name="HasMore">True when the bounded window was filled and another poll from <see cref="NextCursorTicks"/> may return more.</param>
public sealed record NotificationFeedPage(
    IReadOnlyList<NotificationFeedItem> Items,
    long NextCursorTicks,
    bool HasMore);

/// <summary>
/// One vendor-authored message for the desktop's local notification store (flat, display-shaped). The fields mirror
/// the desktop's local <c>NotificationRow</c> so the client apply is a straight copy. <c>AudienceKind</c>/<c>Kind</c>
/// carry the numeric values of the Contracts notification enums (audience: 0 all users, 1 staff role, 2 one user;
/// kind: 0 info, 1 success, 2 warning, 3 promo, 4 system, 5 reward).
/// </summary>
/// <param name="Id">Stable message id (the dedup/upsert key on the client).</param>
/// <param name="AudienceKind">0 = all users, 1 = a staff role, 2 = a single user.</param>
/// <param name="AudienceValue">The role name (kind 1) or the user id (kind 2); null for "all users".</param>
/// <param name="Kind">Message kind as an int (info/success/warning/promo/system/reward).</param>
/// <param name="Tag">Optional short grouping tag.</param>
/// <param name="Title">Message subject.</param>
/// <param name="Body">Message body.</param>
/// <param name="CreatedAtUtc">When the message was authored on the server; drives the pull cursor.</param>
/// <param name="ScheduledForUtc">When the message becomes visible (null = immediately on arrival).</param>
/// <param name="ExpiresAtUtc">When the message stops being shown (null = never).</param>
public sealed record NotificationFeedItem(
    string Id,
    int AudienceKind,
    string? AudienceValue,
    int Kind,
    string? Tag,
    string Title,
    string Body,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ScheduledForUtc,
    DateTimeOffset? ExpiresAtUtc);

/// <summary>
/// The client's real delivery acknowledgement (<c>POST /notifications/receipts</c>): the ids of the messages this
/// device has actually materialised into its local store. The server records one
/// <c>TenantNotificationDelivery(NotificationId, DeviceId)</c> per id (idempotent, duplicate reports are a no-op), so
/// the admin panel can report how many clients truly received a message and re-send to the ones that did not.
/// </summary>
/// <param name="NotificationIds">The stable ids of the messages the reporting device has received and stored.</param>
public sealed record NotificationReceiptReport(IReadOnlyList<string> NotificationIds);
