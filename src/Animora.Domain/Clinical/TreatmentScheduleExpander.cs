using Animora.Domain.Results;

namespace Animora.Domain.Clinical;

/// <summary>How a treatment plan's dates are produced.</summary>
public enum TreatmentScheduleType
{
    /// <summary>One specific day.</summary>
    Once = 0,

    /// <summary>Every N days from a start day, until an end day or a count is reached.</summary>
    EveryNDays = 1,

    /// <summary>A hand-picked set of days.</summary>
    SpecificDates = 2,

    /// <summary>Chosen weekdays within a start..end range.</summary>
    WeeklyOnDays = 3,
}

/// <summary>
/// The parameters of a treatment schedule. Calendar-agnostic: every day is a plain <see cref="DateOnly"/>
/// (the clinic-local day); reading it as Jalali is the presentation layer's job.
/// </summary>
/// <param name="Type">Which expansion rule applies.</param>
/// <param name="Start">First day (Once / EveryNDays / WeeklyOnDays).</param>
/// <param name="End">Inclusive last day (EveryNDays / WeeklyOnDays).</param>
/// <param name="IntervalDays">Gap between occurrences, EveryNDays only.</param>
/// <param name="Count">Number of occurrences, EveryNDays only (alternative to <paramref name="End"/>).</param>
/// <param name="Days">The picked days, SpecificDates only.</param>
/// <param name="WeekdayMask">Bit <c>(int)DayOfWeek</c> per chosen weekday, WeeklyOnDays only.</param>
public sealed record TreatmentSchedule(
    TreatmentScheduleType Type,
    DateOnly? Start = null,
    DateOnly? End = null,
    int? IntervalDays = null,
    int? Count = null,
    IReadOnlyList<DateOnly>? Days = null,
    int WeekdayMask = 0)
{
    /// <summary>One specific day.</summary>
    public static TreatmentSchedule OnceOn(DateOnly day) => new(TreatmentScheduleType.Once, Start: day);

    /// <summary>Every <paramref name="intervalDays"/> days from <paramref name="start"/> until <paramref name="end"/> and/or <paramref name="count"/>.</summary>
    public static TreatmentSchedule Every(DateOnly start, int intervalDays, DateOnly? end = null, int? count = null) =>
        new(TreatmentScheduleType.EveryNDays, start, end, intervalDays, count);

    /// <summary>A hand-picked set of days.</summary>
    public static TreatmentSchedule OnDates(IReadOnlyList<DateOnly> days) =>
        new(TreatmentScheduleType.SpecificDates, Days: days);

    /// <summary>The weekdays in <paramref name="weekdayMask"/> between <paramref name="start"/> and <paramref name="end"/>.</summary>
    public static TreatmentSchedule Weekly(DateOnly start, DateOnly end, int weekdayMask) =>
        new(TreatmentScheduleType.WeeklyOnDays, start, end, WeekdayMask: weekdayMask);

    /// <summary>Bit for <paramref name="day"/> inside a weekday mask.</summary>
    public static int Bit(DayOfWeek day) => 1 << (int)day;
}

/// <summary>
/// Pure expansion of a <see cref="TreatmentSchedule"/> into its ordered, de-duplicated set of days. Bounded by
/// design so a schedule can never grow without limit: at most <see cref="MaxOccurrences"/> days inside a window of
/// <see cref="MaxSpanDays"/>, never more than <see cref="MaxPastDays"/> in the past nor <see cref="MaxFutureDays"/>
/// ahead. An oversized schedule is refused, not truncated.
/// </summary>
public static class TreatmentScheduleExpander
{
    /// <summary>Hard cap on occurrences produced by one plan.</summary>
    public const int MaxOccurrences = 365;

    /// <summary>Hard cap on the distance between the first and the last day.</summary>
    public const int MaxSpanDays = 370;

    /// <summary>How far before "today" the first day may fall (corrects a late entry, blocks a typo).</summary>
    public const int MaxPastDays = 30;

    /// <summary>How far after "today" the last day may fall.</summary>
    public const int MaxFutureDays = 365;

    private const int AllWeekdays = 0b1111111;

    /// <summary>Expands <paramref name="schedule"/> relative to the clinic-local <paramref name="today"/>.</summary>
    public static Result<IReadOnlyList<DateOnly>> Expand(TreatmentSchedule schedule, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        var expanded = schedule.Type switch
        {
            TreatmentScheduleType.Once => ExpandOnce(schedule),
            TreatmentScheduleType.EveryNDays => ExpandEvery(schedule),
            TreatmentScheduleType.SpecificDates => ExpandSpecific(schedule),
            TreatmentScheduleType.WeeklyOnDays => ExpandWeekly(schedule),
            _ => Result<List<DateOnly>>.Fail(ErrorCodes.TreatmentScheduleInvalid, $"Unknown schedule type {schedule.Type}."),
        };
        if (expanded.IsFailure)
        {
            return Result<IReadOnlyList<DateOnly>>.Fail(expanded.Error, expanded.Detail);
        }

        var days = expanded.Value!;
        if (days.Count == 0)
        {
            return Result<IReadOnlyList<DateOnly>>.Fail(ErrorCodes.TreatmentScheduleInvalid, "The schedule produces no dates.");
        }

        days.Sort();
        if (days.Count > MaxOccurrences || days[^1].DayNumber - days[0].DayNumber > MaxSpanDays)
        {
            return Result<IReadOnlyList<DateOnly>>.Fail(ErrorCodes.TreatmentScheduleTooLarge,
                $"A plan holds at most {MaxOccurrences} dates within {MaxSpanDays} days.");
        }

        if (days[0].DayNumber < today.DayNumber - MaxPastDays || days[^1].DayNumber > today.DayNumber + MaxFutureDays)
        {
            return Result<IReadOnlyList<DateOnly>>.Fail(ErrorCodes.TreatmentScheduleInvalid,
                $"Dates must fall between {MaxPastDays} days ago and {MaxFutureDays} days ahead.");
        }

        return Result<IReadOnlyList<DateOnly>>.Ok(days);
    }

    private static Result<List<DateOnly>> ExpandOnce(TreatmentSchedule s) =>
        s.Start is { } day
            ? Result<List<DateOnly>>.Ok([day])
            : Invalid("A one-time schedule needs a date.");

    private static Result<List<DateOnly>> ExpandSpecific(TreatmentSchedule s)
    {
        if (s.Days is null || s.Days.Count == 0)
        {
            return Invalid("Pick at least one date.");
        }

        // Reject absurd input before it is distinct-ed: a caller can only legitimately send MaxOccurrences days.
        if (s.Days.Count > MaxOccurrences * 2)
        {
            return Result<List<DateOnly>>.Fail(ErrorCodes.TreatmentScheduleTooLarge, "Too many picked dates.");
        }

        return Result<List<DateOnly>>.Ok(s.Days.Distinct().ToList());
    }

    private static Result<List<DateOnly>> ExpandEvery(TreatmentSchedule s)
    {
        if (s.Start is not { } start)
        {
            return Invalid("An interval schedule needs a start date.");
        }

        if (s.IntervalDays is not { } step || step < 1 || step > MaxOccurrences)
        {
            return Invalid($"The interval must be 1..{MaxOccurrences} days.");
        }

        if (s.End is null && s.Count is null)
        {
            return Invalid("Give an end date or a number of occurrences.");
        }

        if (s.Count is { } requested && requested < 1)
        {
            return Invalid("The number of occurrences must be positive.");
        }

        if (s.End is { } endDay && endDay < start)
        {
            return Invalid("The end date is before the start date.");
        }

        var limit = Math.Min(s.Count ?? int.MaxValue, MaxOccurrences + 1);
        var days = new List<DateOnly>();
        for (var i = 0; i < limit; i++)
        {
            var offset = (long)i * step;
            if (offset > MaxSpanDays + step)
            {
                break;
            }

            var day = start.AddDays((int)offset);
            if (s.End is { } end && day > end)
            {
                break;
            }

            days.Add(day);
        }

        return Result<List<DateOnly>>.Ok(days);
    }

    private static Result<List<DateOnly>> ExpandWeekly(TreatmentSchedule s)
    {
        if (s.Start is not { } start || s.End is not { } end)
        {
            return Invalid("A weekly schedule needs a start and an end date.");
        }

        if (end < start)
        {
            return Invalid("The end date is before the start date.");
        }

        if (s.WeekdayMask is < 1 or > AllWeekdays)
        {
            return Invalid("Pick at least one weekday.");
        }

        if (end.DayNumber - start.DayNumber > MaxSpanDays)
        {
            return Result<List<DateOnly>>.Fail(ErrorCodes.TreatmentScheduleTooLarge, $"The range exceeds {MaxSpanDays} days.");
        }

        var days = new List<DateOnly>();
        for (var day = start; day <= end; day = day.AddDays(1))
        {
            if ((s.WeekdayMask & TreatmentSchedule.Bit(day.DayOfWeek)) != 0)
            {
                days.Add(day);
            }
        }

        return Result<List<DateOnly>>.Ok(days);
    }

    private static Result<List<DateOnly>> Invalid(string detail) =>
        Result<List<DateOnly>>.Fail(ErrorCodes.TreatmentScheduleInvalid, detail);
}
