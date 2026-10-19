using System.Globalization;
using Animora.Application.Ports;

namespace Animora.Application.Finance;

/// <summary>
/// The shared, host-agnostic implementation of <see cref="IAccountingPeriodBoundaryResolver"/>: maps
/// an instant to the Solar Hijri (Jalali) month that contains it in the clinic time zone, using the
/// platform <see cref="PersianCalendar"/>. Both persistence stacks (local Sqlite and server
/// PostgreSql) register it, so on-demand period provisioning is identical everywhere.
/// </summary>
/// <remarks>
/// <para>
/// The Domain stays calendar-agnostic (CONVENTIONS §3.2); this Application helper is the only place
/// the month is computed for posting. It produces the same <c>yyyy-MM</c> code and real UTC bounds
/// as the first-run seeder's provider, so a lazily-opened period matches a seeded one exactly.
/// </para>
/// <para>
/// The clinic time zone comes from <see cref="IClinicTimeZoneProvider"/> (tenant setting, fallback
/// Asia/Tehran), the same source the scheduling calendar uses — never a direct host look-up here.
/// </para>
/// </remarks>
public sealed class JalaliMonthPeriodBoundaryResolver(IClinicTimeZoneProvider zones) : IAccountingPeriodBoundaryResolver
{
    private static readonly PersianCalendar Calendar = new();

    public AccountingPeriodBoundary Resolve(DateTimeOffset occurredUtc)
    {
        var zone = zones.GetClinicTimeZone();
        var local = TimeZoneInfo.ConvertTime(occurredUtc, zone).DateTime;
        var year = Calendar.GetYear(local);
        var month = Calendar.GetMonth(local);
        var start = Calendar.ToDateTime(year, month, 1, 0, 0, 0, 0);
        var end = month == 12
            ? Calendar.ToDateTime(year + 1, 1, 1, 0, 0, 0, 0)
            : Calendar.ToDateTime(year, month + 1, 1, 0, 0, 0, 0);
        var code = string.Create(CultureInfo.InvariantCulture, $"{year:D4}-{month:D2}");
        return new AccountingPeriodBoundary(code, ToUtc(start, zone), ToUtc(end, zone));
    }

    private static DateTimeOffset ToUtc(DateTime localMidnight, TimeZoneInfo zone) =>
        new(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localMidnight, DateTimeKind.Unspecified), zone),
            TimeSpan.Zero);
}
