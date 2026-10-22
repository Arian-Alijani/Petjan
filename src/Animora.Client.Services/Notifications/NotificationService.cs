using System.Linq.Expressions;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Contracts.Notifications;
using Animora.Contracts.Profile;
using Animora.Domain.Identity;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Client.Services.Notifications;

/// <summary>
/// Real <see cref="INotificationService"/>: a read-only, offline-first receiver feed over the local SQLite store.
/// Messages and rewards are authored by the vendor in the browser admin panel and land in the local store through
/// sync; this service only reads them (audience-filtered for the signed-in user), tracks read receipts, publishes
/// scheduled rows when due (swept on every read and by the optional <see cref="Start">minute timer</see>) and raises
/// <see cref="FeedChanged"/>, the in-process signal the Desktop turns into a UI refresh.
///
/// <para><b>No client authoring.</b> There is deliberately no code path here that creates a message or a
/// <see cref="RewardGrant"/> from user input. The only rows the client writes are automatic, local, read-only
/// system notices (subscription-expiry reminders for the manager) and read receipts.</para>
///
/// <para><b>Rewards.</b> A vendor-granted reward arrives as a <see cref="RewardGrant"/> row (sync landing target)
/// and is applied to the effective subscription by <see cref="RewardOverlay"/>; this service only displays the
/// accompanying message.</para>
///
/// <para>LocalOnly tables carry no global tenant filter, so every query predicates on the tenant explicitly.</para>
/// </summary>
public sealed class NotificationService(
    IServiceScopeFactory scopes,
    SessionContextCache session,
    IClock clock,
    IUserProfileService profile,
    IClinicTimeZoneProvider zones) : INotificationService, IAsyncDisposable
{
    private const int FeedPageSize = 200;
    private const int AudAll = (int)NotificationAudienceKind.AllUsers;
    private const int AudRole = (int)NotificationAudienceKind.Role;
    private const int AudUser = (int)NotificationAudienceKind.SpecificUser;

    /// <summary>Soonest the subscription status is asked for again (it is a server call, never made per read).</summary>
    internal static readonly TimeSpan NoticeCheckInterval = TimeSpan.FromMinutes(30);

    /// <summary>A notice is issued when the subscription ends within this many days.</summary>
    internal const int ExpiryWarningDays = 7;

    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;
    private long _nextNoticeCheckTicks; // UtcTicks; 0 = due now
    private int _noticeCheckRunning;

    /// <inheritdoc />
    public event EventHandler? FeedChanged;

    /// <summary>
    /// Raises <see cref="FeedChanged"/> on behalf of the vendor-message sync: new rows were written straight into the local
    /// store by the pull (not through this service), so the badge/feed must be told to re-read.
    /// </summary>
    public void NotifyFeedChanged() => FeedChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>Starts the once-a-minute due-sweep (idempotent). Called by the composition root after start-up.</summary>
    public void Start() => _loop ??= Task.Run(() => RunAsync(_stop.Token));

    /// <inheritdoc />
    public async Task<NotificationFeed> GetFeedAsync(bool unreadOnly = false, CancellationToken ct = default)
    {
        if (CurrentReader() is not { } viewer) return new NotificationFeed([], 0);
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ILocalTenantContext>().TenantId;
        var now = clock.UtcNow;
        await SweepAsync(db, tenant, now, ct).ConfigureAwait(false);
        await IssueSystemNoticesAsync(db, tenant, viewer, now, ct).ConfigureAwait(false);

        var visible = VisibleTo(tenant, viewer, now);
        var read = db.Set<NotificationReceiptRow>().AsNoTracking()
            .Where(r => r.TenantId == tenant && r.UserId == viewer.UserId && r.ReadAtUtc != null);
        var query = db.Set<NotificationRow>().AsNoTracking().Where(visible);
        if (unreadOnly) query = query.Where(n => !read.Any(r => r.NotificationId == n.Id));

        var rows = await query.OrderByDescending(n => n.PublishedAtUtc).ThenByDescending(n => n.Id)
            .Take(FeedPageSize).ToListAsync(ct).ConfigureAwait(false);
        var pageIds = rows.Select(x => x.Id).ToList();
        var readIds = pageIds.Count == 0
            ? new HashSet<string>(StringComparer.Ordinal)
            : (await read.Where(r => pageIds.Contains(r.NotificationId))
                .Select(r => r.NotificationId).ToListAsync(ct).ConfigureAwait(false))
                .ToHashSet(StringComparer.Ordinal);
        var unread = await CountUnreadAsync(db, tenant, viewer, now, ct).ConfigureAwait(false);
        return new NotificationFeed(rows.Select(n => ToDto(n, readIds.Contains(n.Id))).ToArray(), unread);
    }

    /// <inheritdoc />
    public async Task<int> GetUnreadCountAsync(CancellationToken ct = default)
    {
        if (CurrentReader() is not { } viewer) return 0;
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ILocalTenantContext>().TenantId;
        var now = clock.UtcNow;
        await SweepAsync(db, tenant, now, ct).ConfigureAwait(false);
        await IssueSystemNoticesAsync(db, tenant, viewer, now, ct).ConfigureAwait(false);
        return await CountUnreadAsync(db, tenant, viewer, now, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkReadAsync(string notificationId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(notificationId);
        var viewer = CurrentReader() ?? throw new ServiceException(ErrorCodes.SessionExpired);
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ILocalTenantContext>().TenantId;
        var now = clock.UtcNow;
        // Only a message the viewer can actually see may be marked (a forged id is a no-op, not an error).
        var visible = await db.Set<NotificationRow>().AsNoTracking().Where(VisibleTo(tenant, viewer, now))
            .AnyAsync(n => n.Id == notificationId, ct).ConfigureAwait(false);
        if (!visible) return;
        if (await AddReceiptsAsync(db, tenant, viewer.UserId, [notificationId], now, ct).ConfigureAwait(false))
            FeedChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public async Task MarkAllReadAsync(CancellationToken ct = default)
    {
        var viewer = CurrentReader() ?? throw new ServiceException(ErrorCodes.SessionExpired);
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ILocalTenantContext>().TenantId;
        var now = clock.UtcNow;
        var read = db.Set<NotificationReceiptRow>().AsNoTracking()
            .Where(r => r.TenantId == tenant && r.UserId == viewer.UserId && r.ReadAtUtc != null);
        var unreadIds = await db.Set<NotificationRow>().AsNoTracking().Where(VisibleTo(tenant, viewer, now))
            .Where(n => !read.Any(r => r.NotificationId == n.Id)).Select(n => n.Id)
            .ToListAsync(ct).ConfigureAwait(false);
        if (unreadIds.Count == 0) return;
        if (await AddReceiptsAsync(db, tenant, viewer.UserId, unreadIds, now, ct).ConfigureAwait(false))
            FeedChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        if (_loop is { } loop)
        {
            try { await loop.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }

        _stop.Dispose();
    }

    private async Task RunAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    await TickAsync(ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A failed sweep must never stop the timer; the next minute (or the next read) retries.
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    /// <summary>One timer pass: publish what became due and issue automatic notices for the signed-in user.</summary>
    internal async Task TickAsync(CancellationToken ct)
    {
        if (CurrentReader() is not { } viewer) return;
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ILocalTenantContext>().TenantId;
        var now = clock.UtcNow;
        await SweepAsync(db, tenant, now, ct).ConfigureAwait(false);
        await IssueSystemNoticesAsync(db, tenant, viewer, now, ct).ConfigureAwait(false);
    }

    /// <summary>Makes every scheduled message that is due visible; raises <see cref="FeedChanged"/> when any was.</summary>
    private async Task SweepAsync(AnimoraLocalDbContext db, string tenant, DateTimeOffset now, CancellationToken ct)
    {
        var due = await db.Set<NotificationRow>().AsTracking()
            .Where(n => n.TenantId == tenant && n.PublishedAtUtc == null && n.ScheduledForUtc != null && n.ScheduledForUtc <= now)
            .ToListAsync(ct).ConfigureAwait(false);
        if (due.Count == 0) return;
        foreach (var row in due) row.Publish(now);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        FeedChanged?.Invoke(this, EventArgs.Empty);
    }

    private static async Task<int> CountUnreadAsync(AnimoraLocalDbContext db, string tenant, Reader viewer,
        DateTimeOffset now, CancellationToken ct)
    {
        var read = db.Set<NotificationReceiptRow>().AsNoTracking()
            .Where(r => r.TenantId == tenant && r.UserId == viewer.UserId && r.ReadAtUtc != null);
        return await db.Set<NotificationRow>().AsNoTracking().Where(VisibleTo(tenant, viewer, now))
            .CountAsync(n => !read.Any(r => r.NotificationId == n.Id), ct).ConfigureAwait(false);
    }

    private static async Task<bool> AddReceiptsAsync(AnimoraLocalDbContext db, string tenant, string userId,
        IReadOnlyList<string> idList, DateTimeOffset now, CancellationToken ct)
    {
        var ids = idList.ToList();
        var existing = (await db.Set<NotificationReceiptRow>().AsNoTracking()
                .Where(r => r.TenantId == tenant && r.UserId == userId && ids.Contains(r.NotificationId))
                .Select(r => r.NotificationId).ToListAsync(ct).ConfigureAwait(false))
            .ToHashSet(StringComparer.Ordinal);
        var added = 0;
        foreach (var id in ids)
        {
            if (existing.Contains(id)) continue;
            db.Add(NotificationReceiptRow.Create(tenant, userId, id, now));
            added++;
        }

        if (added == 0) return false;
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // A concurrent read of the same message won the primary key: the end state is identical (idempotent).
            return false;
        }

        return true;
    }

    /// <summary>The audience predicate: published, not expired, and addressed to everyone / the viewer's role / the viewer.</summary>
    private static Expression<Func<NotificationRow, bool>> VisibleTo(string tenant, Reader viewer, DateTimeOffset now)
    {
        var userId = viewer.UserId;
        var role = viewer.Role;
        return n => n.TenantId == tenant && n.PublishedAtUtc != null
            && (n.ExpiresAtUtc == null || n.ExpiresAtUtc > now)
            && (n.AudienceKind == AudAll
                || (n.AudienceKind == AudRole && n.AudienceValue == role)
                || (n.AudienceKind == AudUser && n.AudienceValue == userId));
    }

    private Reader? CurrentReader() => session.Current is { UserId: { Length: > 0 } userId, IsLocked: false } current
        ? new Reader(userId, current.Role?.ToString() ?? string.Empty)
        : null;

    private static NotificationDto ToDto(NotificationRow n, bool isRead) => new(n.Id, (NotificationKind)n.Kind, n.Tag,
        n.Title, n.Body, n.CreatedAtUtc, n.ScheduledForUtc, n.PublishedAtUtc, n.ExpiresAtUtc, isRead);

    internal static string PersianDigits(string ascii)
    {
        var chars = ascii.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
            if (chars[i] is >= '0' and <= '9') chars[i] = (char)('۰' + (chars[i] - '0'));
        return new string(chars);
    }

    private sealed record Reader(string UserId, string Role);

    /// <summary>
    /// Subscription-expiry notices (idempotent, throttled, never on the read path): the first read after
    /// <see cref="NoticeCheckInterval"/> kicks ONE background check; it asks the account authority for the expiry and,
    /// when the subscription ends within <see cref="ExpiryWarningDays"/> days (or already ended), issues a
    /// <see cref="NotificationKind.System"/> notice to the clinic managers - once per (expiry date, stage), guarded by
    /// the unique <c>DedupKey</c>. Only a manager triggers it (they are the audience that can act on it), which also
    /// keeps the server call to one device-user. An unknown/stale status or a failure issues nothing and retries later.
    /// </summary>
    private Task IssueSystemNoticesAsync(AnimoraLocalDbContext db, string tenant, Reader viewer, DateTimeOffset now,
        CancellationToken ct)
    {
        if (viewer.Role != nameof(StaffRole.Manager) || now.UtcTicks < Interlocked.Read(ref _nextNoticeCheckTicks))
            return Task.CompletedTask;
        if (Interlocked.CompareExchange(ref _noticeCheckRunning, 1, 0) != 0) return Task.CompletedTask;

        Interlocked.Exchange(ref _nextNoticeCheckTicks, (now + NoticeCheckInterval).UtcTicks);
        _ = Task.Run(async () =>
        {
            try
            {
                await EnsureSubscriptionExpiryNoticesAsync(tenant, viewer.UserId, _stop.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Best effort: no status / offline / store hiccup. The next interval retries; nothing is half-written.
                Interlocked.Exchange(ref _nextNoticeCheckTicks, 0);
            }
            finally
            {
                Interlocked.Exchange(ref _noticeCheckRunning, 0);
            }
        });
        return Task.CompletedTask;
    }

    /// <summary>Issues the due expiry notices for the current account status; returns how many were created.</summary>
    internal async Task<int> EnsureSubscriptionExpiryNoticesAsync(string tenant, string actorUserId, CancellationToken ct)
    {
        var status = await profile.GetAccountStatusAsync().ConfigureAwait(false);
        if (status.IsStale || status.SubscriptionExpiresAtUtc is not { } expiresAt) return 0; // unknown is not "ending"

        var now = clock.UtcNow;
        var remaining = expiresAt - now;
        string stage;
        NotificationKind kind;
        string title;
        string body;
        var day = ToJalali(expiresAt, zones.GetClinicTimeZone());
        if (remaining <= TimeSpan.Zero)
        {
            stage = "expired";
            kind = NotificationKind.Warning;
            title = "اشتراک شما به پایان رسید";
            body = $"اشتراک کلینیک در تاریخ {day} به پایان رسیده است. برای ادامهٔ استفاده بدون وقفه، اشتراک را تمدید کنید.";
        }
        else if (remaining <= TimeSpan.FromDays(1))
        {
            stage = "last-day";
            kind = NotificationKind.Warning;
            title = "اشتراک شما تا ۲۴ ساعت دیگر تمام می‌شود";
            body = $"اشتراک کلینیک در تاریخ {day} به پایان می‌رسد. همین حالا تمدید کنید تا کار کلینیک متوقف نشود.";
        }
        else if (remaining <= TimeSpan.FromDays(ExpiryWarningDays))
        {
            var days = (int)Math.Ceiling(remaining.TotalDays);
            stage = "soon";
            kind = NotificationKind.System;
            title = "نزدیک شدن به پایان اشتراک";
            body = $"حدود {PersianDigits(days.ToString(System.Globalization.CultureInfo.InvariantCulture))} روز تا پایان اشتراک کلینیک "
                + $"({day}) باقی مانده است. برای جلوگیری از وقفه، اشتراک را تمدید کنید.";
        }
        else
        {
            return 0;
        }

        // One notice per (expiry date, stage): renewing moves the expiry date, which re-arms the whole cycle.
        var key = $"system:sub-expiry:{expiresAt.UtcDateTime:yyyyMMdd}:{stage}";
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        if (await db.Set<NotificationRow>().AsNoTracking()
                .AnyAsync(n => n.TenantId == tenant && n.DedupKey == key, ct).ConfigureAwait(false))
            return 0;

        db.Add(NotificationRow.Create(tenant, AudRole, nameof(StaffRole.Manager), (int)kind, "اشتراک", title, body,
            actorUserId, now, null, now, kind == NotificationKind.Warning && stage == "expired" ? null : expiresAt.AddDays(30),
            key));
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            return 0; // another pass issued the very same notice first (unique DedupKey) - exactly what we wanted
        }

        FeedChanged?.Invoke(this, EventArgs.Empty);
        return 1;
    }

    /// <summary>The Jalali (Persian-calendar) date of an instant in the clinic zone, as «۱۴۰۵/۰۷/۲۰».</summary>
    private static string ToJalali(DateTimeOffset instant, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(instant, zone).DateTime;
        var calendar = new System.Globalization.PersianCalendar();
        var text = string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"{calendar.GetYear(local):D4}/{calendar.GetMonth(local):D2}/{calendar.GetDayOfMonth(local):D2}");
        return PersianDigits(text);
    }
}
