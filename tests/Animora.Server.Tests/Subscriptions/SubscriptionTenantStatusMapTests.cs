using Animora.Domain.Identity;
using Animora.Domain.Subscriptions;
using Xunit;

namespace Animora.Server.Tests.Subscriptions;

public sealed class SubscriptionTenantStatusMapTests
{
    [Theory]
    [InlineData(SubscriptionStatus.Trial, TenantStatus.Active)]
    [InlineData(SubscriptionStatus.Active, TenantStatus.Active)]
    [InlineData(SubscriptionStatus.GracePeriod, TenantStatus.Active)]
    [InlineData(SubscriptionStatus.Suspended, TenantStatus.Suspended)]
    [InlineData(SubscriptionStatus.Expired, TenantStatus.Suspended)]
    [InlineData(SubscriptionStatus.Cancelled, TenantStatus.Closed)]
    public void AllStatuses_Mapped(SubscriptionStatus source, TenantStatus expected) =>
        Assert.Equal(expected, SubscriptionTenantStatusMap.Map(source));

    [Fact]
    public void UnknownStatus_FailClosed() => Assert.Equal(TenantStatus.Suspended,
        SubscriptionTenantStatusMap.Map((SubscriptionStatus)99));
}
