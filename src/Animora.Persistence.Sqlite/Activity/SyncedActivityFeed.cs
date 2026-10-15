using Animora.Application.Activity;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Activity;

/// <summary>
/// Feed rows for records authored by <i>another</i> user and applied through sync (TASK-8 / RC-E). The originating
/// actor travels on the wire record itself (e.g. <c>Appointment.CreatedByUserId</c>); it is stamped onto the row
/// verbatim, so history shows who really did it — never the local session user and never a guess.
/// </summary>
public static class SyncedActivityFeed
{
    /// <summary>Prefix of <see cref="ActivityEvent.CorrelationId"/> on sync-projected rows (survives a feed rebuild).</summary>
    public const string CorrelationPrefix = "sync:";

    /// <summary>
    /// Stages one feed row for <paramref name="draft"/> in the caller's tracked context (caller saves). Idempotent per
    /// (entity type, entity id, title): a row that already exists — from local dispatch or an earlier pull — is kept.
    /// Returns whether a row was staged.
    /// </summary>
    public static async Task<bool> AppendAsync(
        AnimoraLocalDbContext db, ActivityEventDraft draft, TimeZoneInfo zone, string batchId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(zone);
        var tenant = db.CurrentTenantId;
        if (draft.TenantId != tenant) return false;

        // A page can repeat a stable id before SaveChanges; persisted-only checks miss that duplicate.
        if (db.ChangeTracker.Entries<ActivityEvent>().Any(e => e.State == EntityState.Added
            && e.Entity.TenantId == tenant && e.Entity.EntityType == draft.EntityType
            && e.Entity.EntityId == draft.EntityId && e.Entity.Title == draft.TitleKey)) return false;
        var exists = await db.Set<ActivityEvent>().AsNoTracking().AnyAsync(x =>
            x.TenantId == tenant && x.EntityType == draft.EntityType && x.EntityId == draft.EntityId && x.Title == draft.TitleKey, ct)
            .ConfigureAwait(false);
        if (exists) return false;

        var (detail, search) = await ActivityDetailResolver.ResolveAsync(db, draft.DetailArgsJson, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(draft.ActorName))
            draft = draft with { ActorName = await ActivityDetailResolver.ResolveActorNameAsync(db, draft.ActorUserId, null, ct).ConfigureAwait(false) };
        db.Set<ActivityEvent>().Add(ActivityFeedWriter.Create(draft, zone, draft.ActorUserId, CorrelationPrefix + batchId, detail, search));
        return true;
    }
}
