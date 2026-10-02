using Animora.Application.Ports;
using Animora.Contracts.Appointments;
using Animora.Domain.Time;

namespace Animora.Client.Services.Appointments;

/// <summary>
/// <see cref="IClinicClock"/> over <see cref="IClock"/> and the clinic timezone (A24). The zone is hydrated
/// asynchronously once; <see cref="Now"/> itself only does arithmetic, so UI render paths never query the database.
/// </summary>
public sealed class ClinicClockService(IClock clock, IClinicTimeZoneProvider zones) : IClinicClock
{
    // Asia/Tehran fixed offset: the documented default until the persisted zone is known.
    private static readonly TimeZoneInfo Fallback =
        TimeZoneInfo.CreateCustomTimeZone("Asia/Tehran fallback", TimeSpan.FromMinutes(210), "Tehran", "Tehran");

    private volatile TimeZoneInfo? _zone;

    public ClinicMoment Now
    {
        get
        {
            var zone = _zone ?? Fallback;
            var local = TimeZoneInfo.ConvertTime(clock.UtcNow, zone);
            return new ClinicMoment(DateOnly.FromDateTime(local.DateTime), local.Hour * 60 + local.Minute);
        }
    }

    public async Task HydrateAsync(CancellationToken ct = default) =>
        _zone = await zones.GetClinicTimeZoneAsync(ct).ConfigureAwait(false);
}
