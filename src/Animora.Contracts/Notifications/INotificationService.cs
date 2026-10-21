using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Animora.Contracts.Notifications;

/// <summary>
/// Local, offline-first notifications &amp; messages seam. Messages live in the local store, so an admin write is
/// visible at once with no sync; <see cref="FeedChanged"/> is the in-process signal the shell turns into UI refresh.
/// Authorization is enforced inside the implementation (<see cref="CanManage"/> is only a UX hint for the view).
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

    /// <summary>Composes a message for its audience; needs the manage capability, else a service exception.</summary>
    Task CreateAsync(CreateNotificationRequest request, CancellationToken ct = default);

    /// <summary>Records a subscription reward for one user and notifies them; needs the manage capability.</summary>
    Task GrantRewardAsync(GrantRewardRequest request, CancellationToken ct = default);

    /// <summary>Users that may be addressed by a message or reward (needs the manage capability, else empty).</summary>
    Task<IReadOnlyList<NotificationRecipient>> GetRecipientsAsync(CancellationToken ct = default);

    /// <summary>True when the signed-in user may compose messages / grant rewards (UX hint; the service re-checks).</summary>
    bool CanManage { get; }

    /// <summary>
    /// Raised (any thread) after the feed changed for reasons other than the caller's own await: a scheduled message
    /// became due, or a system notice was issued. Subscribers marshal to their own thread.
    /// </summary>
    event EventHandler? FeedChanged;
}
