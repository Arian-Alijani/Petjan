namespace Animora.Persistence.PostgreSql.Outbox;

public static class OutboxMessageTypes
{
    public const string FeedAdvanced = "feed.advanced";
    public const string AuditFlush = "audit.flush";
    public const string LicenseRecheck = "license.recheck";
    public const string LicenseIssue = "license.issue";
    public const string SubscriptionExpiry = "subscription.expiry";
}
