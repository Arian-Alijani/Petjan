namespace Animora.Domain.Clinical;

/// <summary>
/// The authoritative table of vaccination intervals: vaccine code → interval in days <b>after</b>
/// each dose. Index <c>n</c> is the wait after dose <c>n+1</c>; the last entry is the annual booster,
/// which is why every series ends in <c>365</c>.
/// </summary>
/// <remarks>
/// Static data by design: the schedule is a veterinary protocol, not tenant configuration, and the
/// due-date rule must produce the same answer on every device while offline.
/// </remarks>
public static class VaccineSchedule
{
    /// <summary>Interval applied to an unknown code or a dose past the end of a series.</summary>
    public const int AnnualBoosterDays = 365;

    private static readonly Dictionary<string, int[]> Table = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DHPP"] = [21, 21, AnnualBoosterDays],
        ["RABIES"] = [AnnualBoosterDays],
        ["FVRCP"] = [21, 21, AnnualBoosterDays],
        ["FELV"] = [21, AnnualBoosterDays],
        ["LEPTO"] = [21, AnnualBoosterDays],
        ["BORDETELLA"] = [AnnualBoosterDays],
    };

    /// <summary>Every seeded vaccine code with its per-dose interval series, in days.</summary>
    public static IReadOnlyDictionary<string, int[]> All => Table;

    /// <summary>
    /// Days to wait after <paramref name="doseNumber"/> of <paramref name="code"/>. A dose beyond the
    /// end of the series clamps to the last entry (the booster), and an unknown code defaults to
    /// <see cref="AnnualBoosterDays"/> — never to "no due date": a missing follow-up is worse than an
    /// approximate one.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="doseNumber"/> is below 1.</exception>
    public static int IntervalFor(string code, int doseNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentOutOfRangeException.ThrowIfLessThan(doseNumber, 1);

        if (!Table.TryGetValue(code.Trim(), out var series) || series.Length == 0)
        {
            return AnnualBoosterDays;
        }

        var index = Math.Min(doseNumber, series.Length) - 1;
        return series[index];
    }

    /// <summary>Number of doses in the seeded series; <see langword="null"/> for an unknown code.</summary>
    public static int? TotalDosesFor(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        return Table.TryGetValue(code.Trim(), out var series) ? series.Length : null;
    }
}
