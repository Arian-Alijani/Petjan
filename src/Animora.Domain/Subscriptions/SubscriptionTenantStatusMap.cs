using Animora.Domain.Identity;

namespace Animora.Domain.Subscriptions;

public static class SubscriptionTenantStatusMap
{
    public static TenantStatus Map(SubscriptionStatus status) => status switch
    {
        SubscriptionStatus.Trial or SubscriptionStatus.Active or SubscriptionStatus.GracePeriod => TenantStatus.Active,
        SubscriptionStatus.Suspended or SubscriptionStatus.Expired => TenantStatus.Suspended,
        SubscriptionStatus.Cancelled => TenantStatus.Closed,
        _ => TenantStatus.Suspended,
    };
}
