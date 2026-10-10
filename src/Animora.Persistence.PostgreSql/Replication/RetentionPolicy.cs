namespace Animora.Persistence.PostgreSql.Replication;

public static class RetentionPolicy
{
    public const int MaxOfflineDays = 90;
    public const int JournalRetentionDays = MaxOfflineDays + 30;
    public static readonly TimeSpan BatchLifetime = TimeSpan.FromHours(24);
    public static readonly TimeSpan OrphanLifetime = TimeSpan.FromHours(1);
}
