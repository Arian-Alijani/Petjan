using System.Globalization;
using Animora.Application.Ports;
using Animora.Domain.Time;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Client.Services;

/// <summary>
/// The single definition of "today" for every Client.Services seam: the UTC window covering one
/// clinic-local day. Sales KPIs and Cashier stats both use it so two screens never disagree about
/// which payments belong to the current day.
/// </summary>
internal static class ClinicDayWindow
{
    /// <summary>The UTC window [From, To) of the clinic-local day containing <paramref name="now"/>.</summary>
    public static (DateTimeOffset From, DateTimeOffset To) Of(DateTimeOffset now, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(now, zone).Date;
        var start = new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), zone.GetUtcOffset(local));
        return (start.ToUniversalTime(), start.AddDays(1).ToUniversalTime());
    }

    /// <summary>Resolves the clock and clinic zone from a fresh scope and returns today's window.</summary>
    public static (DateTimeOffset From, DateTimeOffset To) Today(IServiceScopeFactory scopes)
    {
        using var scope = scopes.CreateScope();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;
        var zone = scope.ServiceProvider.GetRequiredService<IClinicTimeZoneProvider>().GetClinicTimeZone();
        return Of(now, zone);
    }

    /// <summary>
    /// Parses an ASCII Jalali <c>yyyy/MM/dd</c> bound from the history filter into a UTC instant, honouring
    /// the clinic zone. An empty/unparseable value yields <c>null</c> (unbounded on that side).
    /// </summary>
    public static DateTimeOffset? ParseJalali(string value, TimeZoneInfo tz, bool endOfDay)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var parts = value.Split('/');
        if (parts.Length != 3) return null;
        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var month)
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var day))
        {
            return null;
        }

        try
        {
            var calendar = new PersianCalendar();
            var local = calendar.ToDateTime(year, month, day, endOfDay ? 23 : 0, endOfDay ? 59 : 0, 0, 0);
            var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
            return new DateTimeOffset(unspecified, tz.GetUtcOffset(unspecified)).ToUniversalTime();
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
