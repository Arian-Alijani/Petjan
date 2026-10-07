using Animora.Contracts.Vaccination;

namespace Animora.Desktop.Pages.Vaccination;

/// <summary>
/// Clinic weekend rules for treatment dates. Thursday and Friday are the Iranian weekend: a vaccine / medication day
/// that lands on one of them needs an explicit decision (keep, move to the nearest working day after, or before).
/// The schedule is expanded here exactly as the backend expands it, so the prompt reflects the real dates that would
/// be stored; the backend still validates the final dates.
/// </summary>
public static class TreatmentHolidayPolicy
{
    // Same hard bound as the backend (365 occurrences within a 370-day span) — keeps any loop here finite.
    private const int MaxDays = 400;

    /// <summary>Thursday and Friday.</summary>
    public static bool IsHoliday(DateOnly day) => day.DayOfWeek is DayOfWeek.Thursday or DayOfWeek.Friday;

    /// <summary>The distinct, ordered days a schedule produces (empty when the input cannot produce any).</summary>
    public static IReadOnlyList<DateOnly> Expand(TreatmentScheduleInput schedule)
    {
        var days = new SortedSet<DateOnly>();
        switch (schedule.Type)
        {
            case TreatmentScheduleType.Once when schedule.Start is { } once:
                days.Add(once);
                break;

            case TreatmentScheduleType.SpecificDates when schedule.Days is { } picked:
                foreach (var day in picked.Take(MaxDays))
                {
                    days.Add(day);
                }

                break;

            case TreatmentScheduleType.EveryNDays when schedule.Start is { } start && schedule.IntervalDays is { } step and > 0:
                var limit = Math.Min(schedule.Count ?? MaxDays, MaxDays);
                for (var i = 0; i < limit; i++)
                {
                    var day = start.AddDays(i * step);
                    if (schedule.End is { } end && day > end)
                    {
                        break;
                    }

                    days.Add(day);
                }

                break;

            case TreatmentScheduleType.WeeklyOnDays when schedule.Start is { } from && schedule.End is { } to:
                for (var day = from; day <= to && days.Count < MaxDays; day = day.AddDays(1))
                {
                    if ((schedule.WeekdayMask & (1 << (int)day.DayOfWeek)) != 0)
                    {
                        days.Add(day);
                    }
                }

                break;
        }

        return days.ToList();
    }

    /// <summary>The weekend days among <paramref name="days"/>.</summary>
    public static IReadOnlyList<DateOnly> HolidaysIn(IEnumerable<DateOnly> days) =>
        days.Where(IsHoliday).ToList();

    /// <summary>The nearest working day strictly after (<paramref name="forward"/>) or before <paramref name="day"/>.</summary>
    public static DateOnly NearestWorkingDay(DateOnly day, bool forward)
    {
        var step = forward ? 1 : -1;
        var moved = day.AddDays(step);
        while (IsHoliday(moved))
        {
            moved = moved.AddDays(step);
        }

        return moved;
    }

    /// <summary>
    /// Moves every weekend day to the nearest working day in one uniform direction (working days stay put) and returns
    /// the resulting schedule: a one-time schedule stays one-time, anything else becomes an explicit date list
    /// (collisions with an existing day collapse into one).
    /// </summary>
    public static TreatmentScheduleInput Shift(TreatmentScheduleInput original, IReadOnlyList<DateOnly> days, bool forward)
    {
        var moved = days
            .Select(d => IsHoliday(d) ? NearestWorkingDay(d, forward) : d)
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        return original.Type == TreatmentScheduleType.Once && moved.Count == 1
            ? new TreatmentScheduleInput(TreatmentScheduleType.Once, Start: moved[0])
            : new TreatmentScheduleInput(TreatmentScheduleType.SpecificDates, Days: moved);
    }
}
