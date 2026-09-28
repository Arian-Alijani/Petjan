namespace Animora.Application.UseCases.Scheduling;

/// <summary>
/// The fixed 15-minute slot grid the booking panel offers (C04). A candidate start is on the grid
/// and only valid when the whole <c>[start, start + duration)</c> interval fits inside the doctor's
/// working window — a duration overhang past the window end is never offered.
/// </summary>
public static class SlotGrid
{
    /// <summary>Grid step in minutes.</summary>
    public const int StepMinutes = 15;

    /// <summary>
    /// Every 15-minute start (from <paramref name="windowStartMinute"/>) whose
    /// <c>start + <paramref name="durationMinutes"/></c> is still <c>&lt;= <paramref name="windowEndMinute"/></c>.
    /// Returns nothing when the window or duration is non-positive.
    /// </summary>
    public static IEnumerable<int> StartsWithin(int windowStartMinute, int windowEndMinute, int durationMinutes)
    {
        if (durationMinutes <= 0 || windowEndMinute <= windowStartMinute)
        {
            yield break;
        }

        for (var start = windowStartMinute; start + durationMinutes <= windowEndMinute; start += StepMinutes)
        {
            yield return start;
        }
    }
}
