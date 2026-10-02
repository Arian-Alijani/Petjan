namespace Animora.Application.Ports;

/// <summary>
/// Resolves the clinic's <see cref="TimeZoneInfo"/> — a tenant setting (<c>Tenant.Timezone</c>),
/// fallback <c>"Asia/Tehran"</c>. Every scheduling query/command uses it with
/// <see cref="Animora.Application.Time.ClinicCalendar"/> to turn a local clinic day into a UTC-ticks
/// range (C04). Implemented in the Infrastructure layer so the Application layer stays free of any
/// host/OS time-zone lookup.
/// </summary>
public interface IClinicTimeZoneProvider
{
    /// <summary>
    /// The clinic's time zone; never <see langword="null"/> (falls back to Asia/Tehran). Cheap once hydrated; the
    /// first call on an un-hydrated provider performs a synchronous store read, so UI-facing entry points must
    /// hydrate through <see cref="GetClinicTimeZoneAsync"/> first (A23).
    /// </summary>
    TimeZoneInfo GetClinicTimeZone();

    /// <summary>
    /// Hydrates and returns the clinic time zone without blocking the caller. Providers without an async source
    /// (test doubles) complete synchronously from <see cref="GetClinicTimeZone"/>.
    /// </summary>
    ValueTask<TimeZoneInfo> GetClinicTimeZoneAsync(CancellationToken ct = default) =>
        ValueTask.FromResult(GetClinicTimeZone());

    /// <summary>Drops the cached zone so the next read re-resolves it (after a store restore/rebuild).</summary>
    void Invalidate()
    {
    }
}
