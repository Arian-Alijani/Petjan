using Animora.Domain.Primitives;

namespace Animora.Persistence.Sqlite.Entities;

/// <summary>
/// The per-tenant resume point of the vendor-message pull (<c>GET /notifications</c>): the exclusive UTC-tick cursor of
/// the last applied page. It is advanced in the SAME save that upserts the page's <see cref="NotificationRow"/>s, so a
/// crash mid-page re-reads the page (idempotent upsert) instead of skipping it. <see cref="ReportedTicks"/> is the
/// delivery-receipt watermark: every vendor message created at or before it has been confirmed to the server, so a
/// failed receipt report is retried on a later pass instead of being lost. Device-local, never synced.
/// </summary>
[LocalOnly]
public sealed class NotificationFeedCursor
{
    private NotificationFeedCursor() { }

    /// <summary>Owning tenant (primary key).</summary>
    public string TenantId { get; private set; } = string.Empty;

    /// <summary>The exclusive-resume UTC tick returned by the last applied page.</summary>
    public long CursorTicks { get; private set; }

    /// <summary>Creation tick up to which delivery receipts have been confirmed to the server (0 = none yet).</summary>
    public long ReportedTicks { get; private set; }

    /// <summary>Creates the cursor row.</summary>
    public static NotificationFeedCursor Create(string tenantId, long cursorTicks)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        return new NotificationFeedCursor { TenantId = tenantId, CursorTicks = Math.Max(0, cursorTicks) };
    }

    /// <summary>Moves the cursor forward only; a stale/rewound page never lowers it.</summary>
    public void Advance(long cursorTicks) => CursorTicks = Math.Max(CursorTicks, cursorTicks);

    /// <summary>Moves the receipt watermark forward only (called once every message up to it was confirmed).</summary>
    public void MarkReported(long reportedTicks) => ReportedTicks = Math.Max(ReportedTicks, reportedTicks);
}
