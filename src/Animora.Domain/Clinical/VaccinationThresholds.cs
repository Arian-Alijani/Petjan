namespace Animora.Domain.Clinical;

/// <summary>Due-date projection thresholds; independent of VaccinationRecord.StateAt.</summary>
public static class VaccinationThresholds
{
    public const int OverdueMaxDays = -1;
    public const int DueSoonMaxDays = 14;
    public const int UpcomingMaxDays = 60;

    public static VaccinationDueBucket BucketFor(int daysUntilDue, bool administered)
    {
        if (administered) return VaccinationDueBucket.Done;
        if (daysUntilDue <= OverdueMaxDays) return VaccinationDueBucket.Overdue;
        if (daysUntilDue <= DueSoonMaxDays) return VaccinationDueBucket.DueSoon;
        if (daysUntilDue <= UpcomingMaxDays) return VaccinationDueBucket.Upcoming;
        return VaccinationDueBucket.Scheduled;
    }
}

public enum VaccinationDueBucket
{
    Overdue,
    DueSoon,
    Upcoming,
    Scheduled,
    Done,
}
