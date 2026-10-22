using Animora.Application.Ports;
using Animora.Contracts.Wire.Sync;
using Animora.Domain.Time;
using Animora.Persistence.PostgreSql.Context;
using Animora.Persistence.PostgreSql.Operator;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Repositories;

/// <summary>
/// Vendor-message delivery over the tenant-bound server context. <c>TenantNotifications</c> is a vendor-global table
/// (no automatic tenant filter), so EVERY query here filters on <see cref="AnimoraServerDbContext.CurrentTenantId"/> explicitly.
/// </summary>
public sealed class NotificationFeedStore(AnimoraServerDbContext db, IClock clock) : INotificationFeedStore
{
    /// <summary>Hard ceiling on one window / one receipt batch.</summary>
    internal const int MaxWindow = 200;

    public async Task<NotificationFeedPage> GetFeedAsync(long sinceTicks, int take, CancellationToken ct)
    {
        take = take <= 0 ? 100 : Math.Min(take, MaxWindow);
        var tenantId = db.CurrentTenantId;
        var since = new DateTimeOffset(Math.Clamp(sinceTicks, 0, DateTimeOffset.MaxValue.UtcTicks), TimeSpan.Zero);
        var now = clock.UtcNow;

        IQueryable<TenantNotification> Visible() => db.TenantNotifications.AsNoTracking()
            .Where(n => n.TenantId == tenantId
                && (n.ExpiresAtUtc == null || n.ExpiresAtUtc > now));

        // take+1 detects "more" without a count; ordering by (CreatedAtUtc, Id) is a stable total order.
        var rows = await Visible().Where(n => n.CreatedAtUtc > since)
            .OrderBy(n => n.CreatedAtUtc).ThenBy(n => n.Id).Take(take + 1).ToListAsync(ct).ConfigureAwait(false);

        var hasMore = rows.Count > take;
        if (hasMore)
        {
            // The cursor is an exclusive tick, so never split a group of rows sharing one tick across two pages:
            // drop the trailing partial group; when the whole window is one tick, serve that group completely.
            var boundary = rows[take].CreatedAtUtc.UtcTicks;
            rows.RemoveRange(take, rows.Count - take);
            if (rows[^1].CreatedAtUtc.UtcTicks == boundary)
            {
                rows.RemoveAll(r => r.CreatedAtUtc.UtcTicks == boundary);
                if (rows.Count == 0)
                {
                    var at = new DateTimeOffset(boundary, TimeSpan.Zero);
                    rows = await Visible().Where(n => n.CreatedAtUtc == at).OrderBy(n => n.Id)
                        .Take(MaxWindow).ToListAsync(ct).ConfigureAwait(false);
                }
            }
        }

        var items = rows.Select(n => new NotificationFeedItem(n.Id, n.AudienceKind, n.AudienceValue, n.Kind, n.Tag,
            n.Title, n.Body, n.CreatedAtUtc, n.ScheduledForUtc, n.ExpiresAtUtc)).ToList();
        var next = items.Count == 0 ? sinceTicks : items.Max(i => i.CreatedAtUtc.UtcTicks);
        return new NotificationFeedPage(items, next, hasMore);
    }

    public async Task<int> RecordReceiptsAsync(string deviceId, string userId, IReadOnlyCollection<string> notificationIds, CancellationToken ct)
    {
        var tenantId = db.CurrentTenantId;
        var now = clock.UtcNow;
        var ids = notificationIds.Where(i => !string.IsNullOrWhiteSpace(i) && i.Length <= 26)
            .Distinct(StringComparer.Ordinal).Take(MaxWindow).ToList();
        var written = 0;
        foreach (var id in ids)
        {
            var rowId = Animora.Domain.Primitives.IdGenerator.NewId(now);
            // INSERT..SELECT scopes the id to THIS tenant's messages; ON CONFLICT keeps repeated reports a no-op.
            written += await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO public."TenantNotificationDeliveries" ("Id","TenantId","NotificationId","DeviceId","UserId","ReceivedUtc")
                SELECT {rowId}, n."TenantId", n."Id", {deviceId}, {userId}, {now}
                FROM public."TenantNotifications" n
                WHERE n."Id" = {id} AND n."TenantId" = {tenantId}
                ON CONFLICT ("NotificationId","DeviceId") DO NOTHING
                """, ct).ConfigureAwait(false);
        }
        return written;
    }
}
