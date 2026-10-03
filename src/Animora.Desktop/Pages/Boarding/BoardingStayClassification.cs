using Animora.Contracts.Boarding;

namespace Animora.Desktop.Pages.Boarding;

/// <summary>
/// The single source of truth for how a stay relates to «today» (X14). The card chrome, the status pill, the
/// detail modal and the tab predicate (all / current / leaving / overdue / done) are derived from one instance,
/// so they can never contradict each other.
/// <list type="bullet">
/// <item><b>Overdue</b> — still active but the expected check-out is before today.</item>
/// <item><b>Leaving</b> — active, not overdue, ≤ 1 night remaining (today or tomorrow).</item>
/// <item><b>Current</b> — active, neither overdue nor leaving.</item>
/// </list>
/// </summary>
public readonly record struct BoardingStayClassification(
    int Nights,
    int Elapsed,
    int Remaining,
    int OverdueDays,
    bool IsActive,
    bool IsOverdue,
    bool IsLeaving)
{
    public bool IsCurrent => IsActive && !IsOverdue && !IsLeaving;

    public bool IsDone => !IsActive;

    public static BoardingStayClassification Of(BoardingStay stay, DateOnly today)
    {
        var nights = Math.Max(1, Days(stay.CheckOut, stay.CheckIn));
        var elapsed = Math.Min(Math.Max(0, Days(today, stay.CheckIn)), nights);
        var active = stay.Status == BoardingStatus.Active;
        var remaining = active ? Math.Max(0, Days(stay.CheckOut, today)) : 0;
        var overdueDays = active ? Math.Max(0, Days(today, stay.CheckOut)) : 0;
        var overdue = overdueDays > 0;
        var leaving = active && !overdue && remaining <= 1;
        return new BoardingStayClassification(nights, elapsed, remaining, overdueDays, active, overdue, leaving);
    }

    /// <summary>Whether the stay belongs under the board tab <paramref name="tab"/> (all / current / overdue / leaving / done).</summary>
    public bool MatchesTab(string tab) => tab switch
    {
        "current" => IsCurrent,
        "overdue" => IsOverdue,
        "leaving" => IsLeaving,
        "done" => IsDone,
        _ => true,
    };

    private static int Days(DateOnly to, DateOnly from) => to.DayNumber - from.DayNumber;
}
