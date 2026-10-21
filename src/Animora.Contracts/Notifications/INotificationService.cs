using System;
using System.Threading;
using System.Threading.Tasks;

namespace Animora.Contracts.Notifications;

/// <summary>
/// Read-only, offline-first receiver feed of notifications &amp; messages for the signed-in clinic user. Messages and
/// rewards are authored only by the vendor in the browser admin panel and arrive via sync; the desktop can only read
/// them and mark them read - there is deliberately no compose / grant / reply path on the client.
/// <see cref="FeedChanged"/> is the in-process signal the shell turns into a UI refresh.
/// </summary>
public interface INotificationService
{
    /// <summary>The signed-in user's published, unexpired messages (newest first) and the unread total.</summary>
    Task<NotificationFeed> GetFeedAsync(bool unreadOnly = false, CancellationToken ct = default);

    /// <summary>Unread total for the signed-in user (header/sidebar badge).</summary>
    Task<int> GetUnreadCountAsync(CancellationToken ct = default);

    /// <summary>Marks one message read for the signed-in user (idempotent).</summary>
    Task MarkReadAsync(string notificationId, CancellationToken ct = default);

    /// <summary>Marks every currently visible message read for the signed-in user.</summary>
    Task MarkAllReadAsync(CancellationToken ct = default);

    /// <summary>
    /// Raised (any thread) after the feed changed for reasons other than the caller's own await: a scheduled message
    /// became due, or a system notice was issued. Subscribers marshal to their own thread.
    /// </summary>
    event EventHandler? FeedChanged;
}
