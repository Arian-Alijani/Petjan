using System.Globalization;

namespace Animora.Persistence.Sqlite.Seed;

/// <summary>The opaque code and real UTC range of the accounting period containing an instant.</summary>
public readonly record struct SeedPeriod(string PeriodCode, DateTimeOffset StartUtc, DateTimeOffset EndUtc);

/// <summary>
/// Seed-time platform calendar boundary: maps an injected-clock instant to the accounting period
/// containing it. Domain/Application never compute calendar months themselves.
/// </summary>
public interface ISeedPeriodCodeProvider
{
    SeedPeriod For(DateTimeOffset utcNow);
}

/// <summary>
/// Default provider: the Solar Hijri (Jalali) month in the clinic timezone (Asia/Tehran), computed with
/// the platform <see cref="PersianCalendar"/> — never by formatting a Gregorian date. The code is
/// <c>yyyy-MM</c> of the Jalali month (opaque to the domain); the range is the month's real UTC bounds.
/// </summary>
public sealed class PersianMonthPeriodCodeProvider : ISeedPeriodCodeProvider
{
    private static readonly PersianCalendar Calendar = new();
    private readonly TimeZoneInfo _zone = ResolveZone();

    public SeedPeriod For(DateTimeOffset utcNow)
    {
        var local = TimeZoneInfo.ConvertTime(utcNow, _zone).DateTime;
        var year = Calendar.GetYear(local);
        var month = Calendar.GetMonth(local);
        var start = Calendar.ToDateTime(year, month, 1, 0, 0, 0, 0);
        var end = month == 12
            ? Calendar.ToDateTime(year + 1, 1, 1, 0, 0, 0, 0)
            : Calendar.ToDateTime(year, month + 1, 1, 0, 0, 0, 0);
        var code = string.Create(CultureInfo.InvariantCulture, $"{year:D4}-{month:D2}");
        return new SeedPeriod(code, ToUtc(start), ToUtc(end));
    }

    private DateTimeOffset ToUtc(DateTime localMidnight) =>
        new(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localMidnight, DateTimeKind.Unspecified), _zone), TimeSpan.Zero);

    private static TimeZoneInfo ResolveZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(Animora.Domain.Identity.Tenant.DefaultTimezone); }
        catch (TimeZoneNotFoundException) { return FixedIran(); }
        catch (InvalidTimeZoneException) { return FixedIran(); }
    }

    // Iran has observed a fixed UTC+03:30 offset (no DST) since 2022.
    private static TimeZoneInfo FixedIran() =>
        TimeZoneInfo.CreateCustomTimeZone("IRST", TimeSpan.FromMinutes(210), "IRST", "IRST");
}
