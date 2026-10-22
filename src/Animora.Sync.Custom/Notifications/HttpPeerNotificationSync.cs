using Animora.Application.Ports;
using Animora.Contracts.Diagnostics;
using Animora.Contracts.Wire.Sync;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Sync.Custom.Notifications;

/// <summary>
/// The client consumer of <c>GET /notifications</c>: the vendor messages composed in the admin panel finally reach the
/// desktop. Each page is upserted into the local <see cref="NotificationRow"/> store by the server's stable id (a row
/// that already exists is left untouched, so a re-pull or a second device never duplicates or rewrites a message) and
/// the <see cref="NotificationFeedCursor"/> advances in the SAME save, so a crash mid-page re-reads the page instead of
/// skipping it. After the rows are durable the ids are reported to <c>POST /notifications/receipts</c> so the admin
/// panel counts REAL delivery; a failed report never fails the pull — the cursor's <c>ReportedTicks</c> watermark only
/// advances once every message up to it was confirmed, so unconfirmed ids are re-reported on the next pass (idempotent
/// server-side).
/// <para>Audience filtering is not done here: every message of the tenant is stored and
/// <c>NotificationService.VisibleTo</c> shows each signed-in user only what is addressed to them.</para>
/// <para>Trace lines use the <c>sync.notify</c> category (fetch / applied / receipt / skip).</para>
/// </summary>
public sealed class HttpPeerNotificationSync(SyncFeedClient client, IServiceScopeFactory scopes) : IPeerNotificationSync
{
    /// <summary>Rows requested per page (the server clamps to its own ceiling).</summary>
    internal const int PageSize = 100;

    /// <summary>Pages drained in one pass, so a first sync of a long history never monopolises a sync cycle.</summary>
    internal const int MaxPagesPerPass = 10;

    /// <summary>Ids per receipt request (the server accepts at most 200 per call).</summary>
    internal const int ReceiptBatch = 200;

    private const string Cat = "sync.notify";

    /// <inheritdoc />
    public async Task<Result<int>> PullAsync(CancellationToken ct = default)
    {
        var applied = 0;
        Result<int>? failure = null;

        for (var pageNo = 0; pageNo < MaxPagesPerPass; pageNo++)
        {
            var cursor = await ReadCursorAsync(ct).ConfigureAwait(false);
            var fetched = await client.GetNotificationFeedAsync(cursor, PageSize, ct).ConfigureAwait(false);
            if (fetched.IsFailure)
            {
                TraceLog.Warn($"PeerNotificationSync.Pull — fetch failed ({fetched.Error}) detail='{fetched.Detail}' after {applied} new message(s); cursor stays at {cursor}.", Cat);
                failure = Result<int>.Fail(fetched.Error, fetched.Detail);
                break;
            }

            var page = fetched.Value!;
            TraceLog.Info($"PeerNotificationSync.Pull — fetched {page.Items.Count} message(s) from cursor {cursor} (hasMore={page.HasMore}).", Cat);
            var result = await ApplyAsync(page, ct).ConfigureAwait(false);
            applied += result.Added;
            // Stop on a drained window, or when the server could not move the cursor (defensive: never spin).
            if (!page.HasMore || page.NextCursorTicks <= cursor) break;
        }

        // Report even when a later page failed: whatever IS durable locally should be confirmed (and earlier failures retried).
        await ReportReceiptsAsync(ct).ConfigureAwait(false);

        if (failure is { } failed) return failed;
        TraceLog.Info($"PeerNotificationSync.Pull — applied {applied} new message(s).", Cat);
        return Result<int>.Ok(applied);
    }

    private async Task<long> ReadCursorAsync(CancellationToken ct)
    {
        using var di = scopes.CreateScope();
        var db = di.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var tenant = di.ServiceProvider.GetRequiredService<ILocalTenantContext>().TenantId;
        return await db.Set<NotificationFeedCursor>().AsNoTracking()
            .Where(c => c.TenantId == tenant).Select(c => c.CursorTicks)
            .SingleOrDefaultAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Upserts one page and advances the cursor atomically; returns how many rows were NEW.</summary>
    internal async Task<(int Added, int Skipped)> ApplyAsync(NotificationFeedPage page, CancellationToken ct)
    {
        using var di = scopes.CreateScope();
        var db = di.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var tenant = di.ServiceProvider.GetRequiredService<ILocalTenantContext>().TenantId;
        var now = di.ServiceProvider.GetRequiredService<IClock>().UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

        var items = page.Items.Where(i => !string.IsNullOrWhiteSpace(i.Id)
            && !string.IsNullOrWhiteSpace(i.Title) && !string.IsNullOrWhiteSpace(i.Body)).ToList();
        var ids = items.Select(i => i.Id).Distinct(StringComparer.Ordinal).ToList();
        var existing = (await db.Set<NotificationRow>().AsNoTracking()
            .Where(n => n.TenantId == tenant && ids.Contains(n.Id)).Select(n => n.Id)
            .ToListAsync(ct).ConfigureAwait(false)).ToHashSet(StringComparer.Ordinal);

        int added = 0, skipped = page.Items.Count - items.Count;
        foreach (var item in items)
        {
            if (!existing.Add(item.Id))
            {
                skipped++;
                TraceLog.Info($"PeerNotificationSync.Apply — skip '{item.Id}' (already stored).", Cat);
                continue;
            }

            db.Add(NotificationRow.FromServer(item.Id, tenant, item.AudienceKind, item.AudienceValue, item.Kind, item.Tag,
                item.Title, item.Body, item.CreatedAtUtc, item.ScheduledForUtc, item.ExpiresAtUtc, now));
            added++;
        }

        var cursor = await db.Set<NotificationFeedCursor>().AsTracking().SingleOrDefaultAsync(c => c.TenantId == tenant, ct).ConfigureAwait(false);
        if (cursor is null) db.Add(NotificationFeedCursor.Create(tenant, page.NextCursorTicks));
        else cursor.Advance(page.NextCursorTicks);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        TraceLog.Info($"PeerNotificationSync.Apply — stored {added} new, skipped {skipped}; cursor → {page.NextCursorTicks}.", Cat);
        return (added, skipped);
    }

    /// <summary>
    /// Confirms delivery of every vendor message this device holds that the server has not yet acknowledged (created after
    /// the cursor's <c>ReportedTicks</c> watermark). The watermark moves only after ALL batches succeeded, so a failure at any
    /// point leaves the whole remainder to be re-reported next pass. Failures are logged and swallowed: they never fail the pull.
    /// </summary>
    internal async Task ReportReceiptsAsync(CancellationToken ct)
    {
        try
        {
            using var di = scopes.CreateScope();
            var db = di.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var tenant = di.ServiceProvider.GetRequiredService<ILocalTenantContext>().TenantId;
            var reportedTicks = await db.Set<NotificationFeedCursor>().AsNoTracking()
                .Where(c => c.TenantId == tenant).Select(c => c.ReportedTicks)
                .SingleOrDefaultAsync(ct).ConfigureAwait(false);
            var since = new DateTimeOffset(reportedTicks, TimeSpan.Zero);

            var pending = await db.Set<NotificationRow>().AsNoTracking()
                .Where(n => n.TenantId == tenant && n.CreatedByUserId == NotificationRow.VendorAuthor && n.CreatedAtUtc > since)
                .OrderBy(n => n.CreatedAtUtc).Select(n => new { n.Id, n.CreatedAtUtc })
                .ToListAsync(ct).ConfigureAwait(false);
            if (pending.Count == 0) { TraceLog.Info("PeerNotificationSync.Receipt — nothing new to confirm.", Cat); return; }

            for (var i = 0; i < pending.Count; i += ReceiptBatch)
            {
                var batch = pending.Skip(i).Take(ReceiptBatch).Select(p => p.Id).ToList();
                var reported = await client.ReportNotificationReceiptsAsync(batch, ct).ConfigureAwait(false);
                if (reported.IsFailure)
                {
                    TraceLog.Warn($"PeerNotificationSync.Receipt — report of {batch.Count} id(s) failed ({reported.Error}) detail='{reported.Detail}'; retried next pass.", Cat);
                    return;
                }

                TraceLog.Info($"PeerNotificationSync.Receipt — confirmed {batch.Count} message(s) to the server.", Cat);
            }

            var newest = pending.Max(p => p.CreatedAtUtc.UtcTicks);
            var cursor = await db.Set<NotificationFeedCursor>().AsTracking().SingleOrDefaultAsync(c => c.TenantId == tenant, ct).ConfigureAwait(false);
            if (cursor is null) return; // cannot happen after an applied page; the next pass re-reports idempotently
            cursor.MarkReported(newest);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            TraceLog.Error("PeerNotificationSync.Receipt — report faulted; the pull result is unaffected.", ex, Cat);
        }
    }
}
