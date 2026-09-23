namespace Animora.Domain.Scheduling;

/// <summary>
/// A time slot on the calendar: a start instant plus a length in minutes, modelled as a
/// <b>half-open</b> interval <c>[StartUtc, EndUtc)</c>. Half-open is deliberate — two slots that
/// merely touch (one ends exactly when the next begins) do <i>not</i> overlap, so back-to-back
/// appointments are legal.
/// </summary>
/// <param name="StartUtc">Slot start, UTC.</param>
/// <param name="DurationMinutes">Slot length in minutes.</param>
public readonly record struct AppointmentSlot(DateTimeOffset StartUtc, int DurationMinutes)
{
    /// <summary>Exclusive end instant: <c>StartUtc + DurationMinutes</c>.</summary>
    public DateTimeOffset EndUtc => StartUtc.AddMinutes(DurationMinutes);

    /// <summary>
    /// <see langword="true"/> when this slot and <paramref name="other"/> share any instant.
    /// Half-open test: <c>StartUtc &lt; other.EndUtc &amp;&amp; other.StartUtc &lt; EndUtc</c>, so
    /// adjacent slots (<c>EndUtc == other.StartUtc</c>) never overlap.
    /// </summary>
    public bool Overlaps(AppointmentSlot other) =>
        StartUtc < other.EndUtc && other.StartUtc < EndUtc;
}
