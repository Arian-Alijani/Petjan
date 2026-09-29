using Animora.Application.Ports;
using Animora.Application.Time;
using Animora.Domain.Events;

namespace Animora.Application.Finance;

/// <summary>Maps a relayed business event to its clinic-local day and stages its projection.
/// Called only by the background worker, never by an OLTP event subscription.</summary>
public sealed class FinanceProjectionHandler(IFinanceProjectionStore store, IClinicTimeZoneProvider zones)
{
    public Task HandleAsync(DomainEvent e, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(e);
        var date = ClinicCalendar.LocalDay(e.OccurredAtUtc.UtcTicks, zones.GetClinicTimeZone());
        var day = date.Year * 10000 + date.Month * 100 + date.Day;
        return store.ApplyAsync(e, day, ct);
    }
}
