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
    /// <summary>The clinic's time zone; never <see langword="null"/> (falls back to Asia/Tehran).</summary>
    TimeZoneInfo GetClinicTimeZone();
}
