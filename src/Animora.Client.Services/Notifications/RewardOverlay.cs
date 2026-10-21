using Animora.Contracts.Profile;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;

namespace Animora.Client.Services.Notifications;

/// <summary>
/// Applies locally-granted subscription rewards as a real extension of the effective subscription window.
///
/// <para><b>Why this exists.</b> An administrator's reward (<see cref="RewardGrant"/>) must actually lengthen the
/// subscription the recipient sees — not just leave an audit row. The vendor server (licensing) is the long-term
/// authority, but it is a future track (Datasync, Tracks D-E). Until the grant can be replayed to the server, this
/// overlay makes the reward functional <i>today, offline</i>: every grant row for the user adds its days on top of the
/// authoritative expiry, so <see cref="IUserProfileService.GetAccountStatusAsync"/> returns the extended date at once.</para>
///
/// <para><b>Baseline rules.</b> When the server reports an active subscription expiry, the bonus days are added to it.
/// When the server status is unknown/stale (offline, or no server configured), the reward still stands on its own: the
/// effective expiry is anchored at the latest grant time plus the total bonus days, so a rewarded user is never left
/// with «اطلاعات در دسترس نیست». A later sync phase replays the grants to the server, which then becomes the baseline
/// again and this overlay naturally folds into it.</para>
/// </summary>
internal static class RewardOverlay
{
    /// <summary>Total active reward for a user: (sum of granted days, latest grant time) — defaults when none.</summary>
    internal static async Task<(int Days, DateTimeOffset? LastGrantedAtUtc)> BonusForUserAsync(
        AnimoraLocalDbContext db, string tenant, string userId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(tenant) || string.IsNullOrEmpty(userId)) return (0, null);
        var grants = await db.Set<RewardGrant>().AsNoTracking()
            .Where(g => g.TenantId == tenant && g.TargetUserId == userId)
            .Select(g => new { g.Days, g.GrantedAtUtc })
            .ToListAsync(ct).ConfigureAwait(false);
        if (grants.Count == 0) return (0, null);
        var days = grants.Sum(g => g.Days);
        var last = grants.Max(g => g.GrantedAtUtc);
        return (days, last);
    }

    /// <summary>
    /// Returns <paramref name="status"/> with its subscription expiry extended by the reward bonus. A zero bonus leaves
    /// the status untouched. A positive bonus with no server baseline anchors the expiry at the grant time, so the
    /// reward is usable even while the status is stale/unknown (and no longer reported as stale for expiry purposes).
    /// </summary>
    internal static ClinicAccountStatus Apply(ClinicAccountStatus status, int bonusDays,
        DateTimeOffset? lastGrantedAtUtc, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(status);
        if (bonusDays <= 0) return status;

        // Baseline: the authoritative expiry if we have one and it is still in the future; otherwise anchor on the
        // later of «now» and the last grant time so the full bonus window counts from when the reward was given.
        var baseline = status.SubscriptionExpiresAtUtc is { } expiry && expiry > nowUtc
            ? expiry
            : Max(nowUtc, lastGrantedAtUtc ?? nowUtc);
        var extended = baseline.AddDays(bonusDays);

        return status with
        {
            SubscriptionExpiresAtUtc = extended,
            // The overlay provides a definite local expiry; it must not be treated as "unknown" for expiry purposes.
            IsStale = status.SubscriptionExpiresAtUtc is null ? false : status.IsStale,
        };
    }

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a >= b ? a : b;
}
