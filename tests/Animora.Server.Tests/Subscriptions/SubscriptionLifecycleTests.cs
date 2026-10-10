using Animora.Domain.Results;
using Animora.Domain.Subscriptions;
using Xunit;

namespace Animora.Server.Tests.Subscriptions;

public sealed class SubscriptionLifecycleTests
{
    [Fact]
    public void AllowedAndForbiddenTransitions_Table()
    {
        // Rows/columns: Trial, Active, GracePeriod, Suspended, Cancelled, Expired.
        bool[][] table = [
            [false, true, true, true, true, true],
            [false, true, true, true, true, false],
            [false, true, false, true, true, true],
            [false, true, false, false, true, true],
            [false, false, false, false, false, false],
            [false, true, false, false, true, false],
        ];
        foreach (var from in Enum.GetValues<SubscriptionStatus>())
        foreach (var to in Enum.GetValues<SubscriptionStatus>())
        {
            var result = SubscriptionLifecycle.EnsureAllowed(from, to);
            Assert.Equal(table[(int)from][(int)to], result.IsSuccess);
            if (result.IsFailure) Assert.Equal(ErrorCodes.SubscriptionInvalidTransition, result.Error);
        }
        Assert.True(SubscriptionLifecycle.EnsureAllowed((SubscriptionStatus)99, SubscriptionStatus.Active).IsFailure);
    }
}
