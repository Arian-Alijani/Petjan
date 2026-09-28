namespace Animora.Application.Time;

/// <summary>
/// The single authority for converting between a <b>local clinic day</b> (<see cref="DateOnly"/> +
/// minutes-from-midnight) and <b>UTC ticks</b> (how <c>Appointment.StartUtc</c> is stored and
/// indexed). Every date-ranged scheduling query converts once, here, then filters purely on
/// <c>&gt;= startTicks &amp;&amp; &lt; endTicks</c> against the UTC index — never a SQL date
/// function (CONVENTIONS §5). Jalali rendering never happens here (R-B24): this speaks only
/// <see cref="DateOnly"/>, minutes and ticks.
/// </summary>
public static class ClinicCalendar
{
    /// <summary>
    /// The half-open UTC-ticks range <c>[StartTicks, EndTicks)</c> covering the whole local
    /// <paramref name="localDay"/> in <paramref name="tz"/>.
    /// </summary>
    public static (long StartTicks, long EndTicks) DayRangeUtc(DateOnly localDay, TimeZoneInfo tz)
    {
        ArgumentNullException.ThrowIfNull(tz);
        var start = ToUtcTicks(localDay, 0, tz);
        var end = ToUtcTicks(localDay.AddDays(1), 0, tz);
        return (start, end);
    }

    /// <summary>
    /// Converts a local <paramref name="localDay"/> + <paramref name="minutesFromMidnight"/> to UTC ticks.
    /// </summary>
    public static long ToUtcTicks(DateOnly localDay, int minutesFromMidnight, TimeZoneInfo tz)
    {
        ArgumentNullException.ThrowIfNull(tz);
        var local = DateTime.SpecifyKind(
            localDay.ToDateTime(TimeOnly.MinValue).AddMinutes(minutesFromMidnight),
            DateTimeKind.Unspecified);
        var utc = TimeZoneInfo.ConvertTimeToUtc(local, tz);
        return new DateTimeOffset(utc, TimeSpan.Zero).UtcTicks;
    }

    /// <summary>The local day that <paramref name="utcTicks"/> falls on in <paramref name="tz"/>.</summary>
    public static DateOnly LocalDay(long utcTicks, TimeZoneInfo tz)
    {
        ArgumentNullException.ThrowIfNull(tz);
        var local = TimeZoneInfo.ConvertTimeFromUtc(new DateTime(utcTicks, DateTimeKind.Utc), tz);
        return DateOnly.FromDateTime(local);
    }

    /// <summary>
    /// Minutes from the start of <paramref name="localDay"/> to <paramref name="utcTicks"/>, in
    /// <paramref name="tz"/> local time (the inverse of <see cref="ToUtcTicks"/>).
    /// </summary>
    public static int MinutesFromDayStart(long utcTicks, DateOnly localDay, TimeZoneInfo tz)
    {
        ArgumentNullException.ThrowIfNull(tz);
        var local = TimeZoneInfo.ConvertTimeFromUtc(new DateTime(utcTicks, DateTimeKind.Utc), tz);
        var dayStart = localDay.ToDateTime(TimeOnly.MinValue);
        return (int)Math.Round((local - dayStart).TotalMinutes);
    }
}
