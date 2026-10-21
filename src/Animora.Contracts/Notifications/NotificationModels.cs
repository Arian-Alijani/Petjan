using System;
using System.Collections.Generic;

namespace Animora.Contracts.Notifications;

/// <summary>Visual/semantic type of a message; the desktop maps it to an icon + accent colour.</summary>
public enum NotificationKind
{
    Info = 0,
    Success = 1,
    Warning = 2,
    Promo = 3,
    System = 4,
    Reward = 5,
}

/// <summary>Who a message is addressed to.</summary>
public enum NotificationAudienceKind
{
    /// <summary>Every user of the clinic.</summary>
    AllUsers = 0,

    /// <summary>Every user holding the staff role named by <c>AudienceValue</c> (a <c>UserRole</c> name).</summary>
    Role = 1,

    /// <summary>One user, identified by <c>AudienceValue</c> (the user id).</summary>
    SpecificUser = 2,
}

/// <summary>One delivered message as the signed-in user sees it.</summary>
/// <param name="Id">Stable ULID of the message.</param>
/// <param name="Kind">Semantic type (icon/colour).</param>
/// <param name="Tag">Optional short label shown as a coloured chip.</param>
/// <param name="Title">Headline.</param>
/// <param name="Body">Message text.</param>
/// <param name="CreatedAtUtc">When the message was composed.</param>
/// <param name="ScheduledForUtc">The planned publish time when it was scheduled, else null.</param>
/// <param name="PublishedAtUtc">When it became visible to its audience (null while still scheduled).</param>
/// <param name="ExpiresAtUtc">When it stops being shown, or null.</param>
/// <param name="IsRead">Whether the signed-in user has opened/acknowledged it.</param>
public sealed record NotificationDto(
    string Id,
    NotificationKind Kind,
    string? Tag,
    string Title,
    string Body,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ScheduledForUtc,
    DateTimeOffset? PublishedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    bool IsRead);

/// <summary>Compose request (needs the manage capability). A null <see cref="ScheduledForUtc"/> publishes immediately.</summary>
public sealed record CreateNotificationRequest(
    NotificationAudienceKind AudienceKind,
    string? AudienceValue,
    NotificationKind Kind,
    string? Tag,
    string Title,
    string Body,
    DateTimeOffset? ScheduledForUtc,
    DateTimeOffset? ExpiresAtUtc);

/// <summary>Grants <paramref name="Days"/> of subscription to one user as a reward (needs the manage capability).</summary>
public sealed record GrantRewardRequest(string TargetUserId, int Days, string Reason);

/// <summary>The signed-in user's feed plus the unread total (badge source).</summary>
public sealed record NotificationFeed(IReadOnlyList<NotificationDto> Items, int UnreadCount);

/// <summary>A user a message or reward can be addressed to (compose/reward pickers).</summary>
public sealed record NotificationRecipient(string UserId, string Name, string RoleLabel);
