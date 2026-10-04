using Animora.Application.Activity;
using Animora.Application.Correlation;
using Animora.Application.Ports;
using Animora.Application.Time;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;

namespace Animora.Persistence.Sqlite.Activity;

/// <summary>Stages a feed row in the same tracked DbContext as the source aggregate.</summary>
public sealed class ActivityFeedWriter(
    AnimoraLocalDbContext db, IClinicTimeZoneProvider zones, ICorrelationContext correlation) : IActivityFeedWriter
{
    public async Task WriteAsync(ActivityEventDraft draft, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (draft.TenantId != db.CurrentTenantId)
            throw new InvalidOperationException("Activity event belongs to another tenant.");
        var (detail, search) = await ActivityDetailResolver.ResolveAsync(db, draft.DetailArgsJson, ct);
        db.Set<ActivityEvent>().Add(Create(draft, zones.GetClinicTimeZone(),
            correlation.Current?.UserId, correlation.CurrentId, detail, search));
    }

    /// <summary>Also used for rebuilding the same projection from the event stream.</summary>
    public static ActivityEvent Create(ActivityEventDraft draft, TimeZoneInfo zone,
        string? actor = null, string? correlationId = null, string? detailText = null, string? searchText = null)
    {
        // Same normalization for the live path and the rebuild: blank means "unknown", the event's own
        // actor (when it carries one) is the only fallback — a clinician is never guessed.
        actor = string.IsNullOrWhiteSpace(actor) ? null : actor;
        var day = ClinicCalendar.LocalDay(draft.OccurredUtc.UtcTicks, zone);
        return ActivityEvent.Create(draft.TenantId, draft.OccurredUtc,
            day.Year * 10000 + day.Month * 100 + day.Day,
            draft.Kind.ToString(), draft.TitleKey, draft.DetailArgsJson,
            draft.EntityType, draft.EntityId, draft.AmountMinor, draft.AmountSign,
            draft.Status.ToString(), actor ?? draft.ActorUserId, draft.ActorName,
            correlationId ?? draft.CorrelationId, draft.EventId, detailText,
            searchText is { Length: > 0 } ? $"{searchText} {draft.EntityId} {draft.ActorName}".Trim() : null);
    }
}
