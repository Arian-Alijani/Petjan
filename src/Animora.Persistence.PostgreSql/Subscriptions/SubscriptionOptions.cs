namespace Animora.Persistence.PostgreSql.Subscriptions;

public sealed class SubscriptionOptions
{
    public const string SectionName = "Subscription";
    public int GraceDays { get; set; } = 7;
    public long StandardMonthlyAmountMinor { get; set; }
}
