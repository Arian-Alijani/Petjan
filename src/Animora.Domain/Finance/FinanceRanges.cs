using Animora.Domain.Results;

namespace Animora.Domain.Finance;

/// <summary>A calendar-day window, inclusive at both ends. Days are encoded as yyyyMMdd.</summary>
public readonly record struct FinanceDayRange(int FromDay, int ToDay);

public sealed record FinanceRangeDefinition(string Id, string Label, int Days, string Bucket);

/// <summary>Finance window catalog. All offsets are applied to dates, never to yyyyMMdd integers.</summary>
public static class FinanceRanges
{
    private static readonly FinanceRangeDefinition[] Definitions =
    [
        new("today", "امروز", 1, "day"),
        new("7d", "۷ روز گذشته", 7, "day"),
        new("30d", "۳۰ روز گذشته", 30, "day"),
        new("90d", "۹۰ روز گذشته", 90, "week"),
        new("ytd", "از ابتدای سال", 0, "month"),
        new("custom", "بازه دلخواه", 0, "day"),
    ];

    public static IReadOnlyList<FinanceRangeDefinition> All => Definitions;

    public static Result<FinanceDayRange> Resolve(
        string? id, int todayLocalDay, int? customFromDay = null, int? customToDay = null)
    {
        if (!TryParseDay(todayLocalDay, out var today))
            return Invalid("Invalid local today");

        if (string.Equals(id, "custom", StringComparison.Ordinal))
        {
            if (!customFromDay.HasValue || !customToDay.HasValue ||
                !TryParseDay(customFromDay.Value, out var from) ||
                !TryParseDay(customToDay.Value, out var to) || from > to)
                return Invalid("Invalid custom window");
            return Result<FinanceDayRange>.Ok(new(customFromDay.Value, customToDay.Value));
        }

        var start = id switch
        {
            "today" => today,
            "7d" => today.AddDays(-6),
            "30d" => today.AddDays(-29),
            "90d" => today.AddDays(-89),
            "ytd" => new DateOnly(today.Year, 1, 1),
            _ => DateOnly.MinValue,
        };
        return start == DateOnly.MinValue
            ? Invalid($"Unknown range: {id}")
            : Result<FinanceDayRange>.Ok(new(ToDay(start), todayLocalDay));
    }

    private static Result<FinanceDayRange> Invalid(string detail) =>
        Result<FinanceDayRange>.Fail(ErrorCodes.FinanceRangeInvalid, detail);

    private static int ToDay(DateOnly day) => day.Year * 10000 + day.Month * 100 + day.Day;

    private static bool TryParseDay(int value, out DateOnly day) =>
        DateOnly.TryParseExact(value.ToString("D8", System.Globalization.CultureInfo.InvariantCulture),
            "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out day);
}
