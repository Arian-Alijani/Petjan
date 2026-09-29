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
    public Task WriteAsync(ActivityEventDraft draft, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (draft.TenantId != db.CurrentTenantId)
            throw new InvalidOperationException("Activity event belongs to another tenant.");
        db.Set<ActivityEvent>().Add(Create(draft, zones.GetClinicTimeZone(),
            correlation.Current?.UserId, correlation.CurrentId));
        return Task.CompletedTask;
    }

    /// <summary>Also used for rebuilding the same projection from the event stream.</summary>
    public static ActivityEvent Create(ActivityEventDraft draft, TimeZoneInfo zone,
        string? actor = null, string? correlationId = null)
    {
        var day = ClinicCalendar.LocalDay(draft.OccurredUtc.UtcTicks, zone);
        return ActivityEvent.Create(draft.TenantId, draft.OccurredUtc,
            day.Year * 10000 + day.Month * 100 + day.Day,
            draft.Kind.ToString(), draft.TitleKey, draft.DetailArgsJson,
            draft.EntityType, draft.EntityId, draft.AmountMinor, draft.AmountSign,
            draft.Status.ToString(), actor ?? draft.ActorUserId, draft.ActorName,
            correlationId ?? draft.CorrelationId);
    }
}
