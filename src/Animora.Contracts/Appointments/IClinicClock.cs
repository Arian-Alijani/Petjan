namespace Animora.Contracts.Appointments;

/// <summary>A clinic-local instant: the clinic's calendar day and minutes since its local midnight (A24).</summary>
public readonly record struct ClinicMoment(DateOnly Today, int Minutes);

/// <summary>
/// The clinic's own clock for the appointments UI. "Today" and "now" are the clinic's local day/minute (from the
/// system clock and the clinic timezone), never the device's local time, so a device in another zone shows the same
/// day, history cutoff and live badges as the clinic.
/// </summary>
public interface IClinicClock
{
    /// <summary>
    /// The current clinic-local moment. Never blocks: before <see cref="HydrateAsync"/> has resolved the clinic
    /// timezone it uses the Asia/Tehran default, afterwards the persisted zone.
    /// </summary>
    ClinicMoment Now { get; }

    /// <summary>Convenience for <c>Now.Today</c>.</summary>
    DateOnly Today => Now.Today;

    /// <summary>Resolves the clinic timezone asynchronously (cached); call once after the store is ready.</summary>
    Task HydrateAsync(CancellationToken ct = default);
}
