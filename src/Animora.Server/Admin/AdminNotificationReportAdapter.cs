using Animora.Domain.Identity;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Persistence.PostgreSql.Operator;
using Animora.Web.Admin.Models;
using Animora.Web.Admin.Ports;
using Microsoft.EntityFrameworkCore;
namespace Animora.Server.Admin;

/// <summary>
/// The admin delivery report: how many of a clinic's ACTIVE devices truly received each vendor message (a
/// <see cref="TenantNotificationDelivery"/> receipt is written only after the desktop stored the message locally), plus the
/// resend action for the devices that did not. Read-only apart from the resend, which re-uses the audited send path.
/// </summary>
internal sealed class AdminNotificationReportAdapter(AnimoraOperatorDbContext db, OperatorNotificationAdministration notifications,
    AdminSession session, IHttpContextAccessor accessor, IClock clock) : IAdminNotificationReport
{
    private const int MaxTake = 100;

    public async Task<Result<IReadOnlyList<NotificationDeliveryReportRow>>> RecentAsync(string? tenantId, int take, CancellationToken ct = default)
    {
        _ = session.OperatorId; // a cookie-authenticated operator is mandatory
        take = Math.Clamp(take, 1, MaxTake);
        var tenant = string.IsNullOrWhiteSpace(tenantId) ? null : tenantId.Trim();
        if (tenant is { Length: > 26 }) return Result<IReadOnlyList<NotificationDeliveryReportRow>>.Fail(ErrorCodes.ValidationFailed);

        var messages = await db.TenantNotifications.AsNoTracking()
            .Where(n => tenant == null || n.TenantId == tenant)
            .OrderByDescending(n => n.CreatedAtUtc).ThenByDescending(n => n.Id).Take(take)
            .Select(n => new { n.Id, n.TenantId, n.Title, n.Kind, n.CreatedAtUtc })
            .ToListAsync(ct).ConfigureAwait(false);
        if (messages.Count == 0) return Result<IReadOnlyList<NotificationDeliveryReportRow>>.Ok([]);

        var ids = messages.Select(m => m.Id).ToList();
        var tenantIds = messages.Select(m => m.TenantId).Distinct().ToList();

        var targets = (await db.Devices.AsNoTracking()
            .Where(d => tenantIds.Contains(d.TenantId) && !d.Deleted && d.Status == DeviceStatus.Active)
            .GroupBy(d => d.TenantId).Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToListAsync(ct).ConfigureAwait(false)).ToDictionary(x => x.TenantId, x => x.Count, StringComparer.Ordinal);

        // Only receipts from devices that are STILL active count, so Delivered never exceeds Target after a device is revoked.
        var delivered = (await (
                from r in db.TenantNotificationDeliveries.AsNoTracking()
                join d in db.Devices.AsNoTracking() on r.DeviceId equals d.Id
                where ids.Contains(r.NotificationId) && !d.Deleted && d.Status == DeviceStatus.Active
                group r by r.NotificationId into g
                select new { NotificationId = g.Key, Count = g.Count(), First = g.Min(x => x.ReceivedUtc), Last = g.Max(x => x.ReceivedUtc) })
            .ToListAsync(ct).ConfigureAwait(false)).ToDictionary(x => x.NotificationId, StringComparer.Ordinal);

        var names = (await db.Tenants.AsNoTracking().Where(t => tenantIds.Contains(t.Id))
            .Select(t => new { t.Id, t.DisplayName, t.Code }).ToListAsync(ct).ConfigureAwait(false))
            .ToDictionary(t => t.Id, t => string.IsNullOrWhiteSpace(t.DisplayName) ? t.Code : t.DisplayName, StringComparer.Ordinal);

        var rows = messages.Select(m =>
        {
            delivered.TryGetValue(m.Id, out var d);
            return new NotificationDeliveryReportRow(m.Id, m.Title, m.Kind, m.CreatedAtUtc,
                targets.GetValueOrDefault(m.TenantId), d?.Count ?? 0, d?.First, d?.Last, m.TenantId, names.GetValueOrDefault(m.TenantId));
        }).ToList();
        return Result<IReadOnlyList<NotificationDeliveryReportRow>>.Ok(rows);
    }

    public async Task<Result<MutationReceipt>> ResendUndeliveredAsync(string notificationId, CancellationToken ct = default)
    {
        _ = session.OperatorId;
        if (string.IsNullOrWhiteSpace(notificationId) || notificationId.Length > 26)
            return Result<MutationReceipt>.Fail(ErrorCodes.ValidationFailed);

        var original = await db.TenantNotifications.AsNoTracking().SingleOrDefaultAsync(n => n.Id == notificationId, ct).ConfigureAwait(false);
        if (original is null) return Result<MutationReceipt>.Fail(ErrorCodes.NotFound);

        var now = clock.UtcNow;
        if (original.ExpiresAtUtc is { } expires && expires <= now) return Result<MutationReceipt>.Fail(ErrorCodes.ValidationFailed);

        var report = await RecentForAsync(original, ct).ConfigureAwait(false);
        // Only worth a copy when somebody is still missing it (the report UI shows the button under the same condition).
        if (!report.HasUndelivered) return Result<MutationReceipt>.Fail(ErrorCodes.ValidationFailed);

        // A fresh copy for the same clinic and audience: devices that have not pulled it yet get it on their next pass. A schedule that
        // already passed is dropped (the copy is immediately visible); a future one is kept.
        var scheduled = original.ScheduledForUtc is { } at && at > now ? at : (DateTimeOffset?)null;
        var draft = new OperatorNotificationDraft(original.AudienceKind, original.AudienceValue, original.Kind, original.Tag,
            original.Title, original.Body, scheduled, original.ExpiresAtUtc);
        var correlation = accessor.HttpContext?.TraceIdentifier ?? Guid.NewGuid().ToString("N");
        var sent = await notifications.SendAsync(original.TenantId, draft, session.Actor, correlation, ct).ConfigureAwait(false);
        return sent.Map(_ => new MutationReceipt(null, original.TenantId));
    }

    private async Task<NotificationDeliveryReportRow> RecentForAsync(TenantNotification original, CancellationToken ct)
    {
        var target = await db.Devices.AsNoTracking()
            .CountAsync(d => d.TenantId == original.TenantId && !d.Deleted && d.Status == DeviceStatus.Active, ct).ConfigureAwait(false);
        var got = await (
                from r in db.TenantNotificationDeliveries.AsNoTracking()
                join d in db.Devices.AsNoTracking() on r.DeviceId equals d.Id
                where r.NotificationId == original.Id && !d.Deleted && d.Status == DeviceStatus.Active
                select r.Id)
            .CountAsync(ct).ConfigureAwait(false);
        return new NotificationDeliveryReportRow(original.Id, original.Title, original.Kind, original.CreatedAtUtc,
            target, got, null, null, original.TenantId, null);
    }
}
