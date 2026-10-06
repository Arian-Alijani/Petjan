using System.Globalization;
using Animora.Application.Ports;
using Animora.Application.Time;
using Animora.Domain.Finance;
using Animora.Domain.Results;
using Animora.Domain.Time;

namespace Animora.Application.UseCases.Finance;

/// <summary>
/// A requested «آمار» window: one of <see cref="FinanceRanges.All"/> ids plus the custom bounds
/// (Gregorian <c>yyyyMMdd</c>) when the id is <c>custom</c>.
/// </summary>
public sealed record StatisticsWindow(string RangeId = "30d", int? CustomFromDay = null, int? CustomToDay = null);

/// <summary>A resolved window: the inclusive day range, its chart bucket and the clinic-local today.</summary>
public sealed record ResolvedStatisticsWindow(FinanceDayRange Range, string Bucket, int TodayLocalDay);

/// <summary>
/// Resolves a <see cref="StatisticsWindow"/> against the clinic clock. <b>Decision (Task 1):</b> the
/// <c>ytd</c> window («از ابتدای سال») starts at the first day of the current <b>Jalali</b> year,
/// computed here in the Application layer with <see cref="PersianCalendar"/> and passed to the
/// calendar-agnostic <see cref="FinanceRanges.Resolve"/> through its <c>custom</c> branch; the
/// Domain's own Gregorian <c>ytd</c> branch is never used by the statistics screen.
/// </summary>
public static class StatisticsWindowResolver
{
    private static readonly PersianCalendar Jalali = new();

    public static Result<ResolvedStatisticsWindow> Resolve(StatisticsWindow window, IClock clock, IClinicTimeZoneProvider zones)
    {
        ArgumentNullException.ThrowIfNull(window);
        var today = ClinicCalendar.LocalDay(clock.UtcNow.UtcTicks, zones.GetClinicTimeZone());
        var todayKey = Key(today);
        var id = string.IsNullOrWhiteSpace(window.RangeId) ? "30d" : window.RangeId.Trim();
        var definition = FinanceRanges.All.FirstOrDefault(r => r.Id == id);
        if (definition is null)
            return Result<ResolvedStatisticsWindow>.Fail(ErrorCodes.FinanceRangeInvalid, $"Unknown range: {id}");

        var resolved = id == "ytd"
            ? FinanceRanges.Resolve("custom", todayKey, Key(JalaliYearStart(today)), todayKey)
            : FinanceRanges.Resolve(id, todayKey, window.CustomFromDay, window.CustomToDay);
        if (resolved.IsFailure) return Result<ResolvedStatisticsWindow>.Fail(resolved.Error, resolved.Detail);

        var range = resolved.Value;
        var bucket = definition.Bucket;
        if (id == "custom")
        {
            var days = Parse(range.ToDay).DayNumber - Parse(range.FromDay).DayNumber + 1;
            if (days > 1100)
                return Result<ResolvedStatisticsWindow>.Fail(ErrorCodes.FinanceRangeInvalid, "Custom window longer than 3 years");
            bucket = days <= 45 ? "day" : days <= 200 ? "week" : "month";
        }
        return Result<ResolvedStatisticsWindow>.Ok(new(range, bucket, todayKey));
    }

    /// <summary>The Gregorian date of 1 Farvardin of the Jalali year containing <paramref name="day"/>.</summary>
    public static DateOnly JalaliYearStart(DateOnly day) =>
        DateOnly.FromDateTime(Jalali.ToDateTime(Jalali.GetYear(day.ToDateTime(TimeOnly.MinValue)), 1, 1, 0, 0, 0, 0));

    internal static int Key(DateOnly day) => day.Year * 10000 + day.Month * 100 + day.Day;
    internal static DateOnly Parse(int day) => new(day / 10000, day / 100 % 100, day % 100);
}
