namespace Animora.Domain.Finance;

/// <summary>Pure, clinic-local date rules for actionable cheque alerts.</summary>
public static class ChequeAlertRules
{
    public const int DueWithinDays = 3;

    public static bool DueWithin3Days(ChequeState state, DateOnly due, DateOnly today) =>
        IsUncleared(state) && due.DayNumber >= today.DayNumber &&
        due.DayNumber - today.DayNumber <= DueWithinDays;

    public static bool OverdueUncleared(ChequeState state, DateOnly due, DateOnly today) =>
        IsUncleared(state) && due.DayNumber < today.DayNumber;

    public static bool BouncedUnresolved(ChequeState state) => state == ChequeState.Bounced;

    private static bool IsUncleared(ChequeState state) =>
        state is ChequeState.Registered or ChequeState.InPortfolio or ChequeState.Deposited;
}
