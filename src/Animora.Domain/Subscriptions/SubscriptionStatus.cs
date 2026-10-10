namespace Animora.Domain.Subscriptions;

public enum SubscriptionStatus : short
{
    Trial = 0, Active = 1, GracePeriod = 2, Suspended = 3, Cancelled = 4, Expired = 5,
}
