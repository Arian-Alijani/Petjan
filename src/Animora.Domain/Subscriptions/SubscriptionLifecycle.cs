using Animora.Domain.Results;

namespace Animora.Domain.Subscriptions;

public static class SubscriptionLifecycle
{
    private static readonly IReadOnlyDictionary<SubscriptionStatus, SubscriptionStatus[]> Transitions =
        new Dictionary<SubscriptionStatus, SubscriptionStatus[]>
        {
            [SubscriptionStatus.Trial] = [SubscriptionStatus.Active, SubscriptionStatus.Suspended,
                SubscriptionStatus.Cancelled, SubscriptionStatus.GracePeriod, SubscriptionStatus.Expired],
            [SubscriptionStatus.Active] = [SubscriptionStatus.GracePeriod, SubscriptionStatus.Suspended,
                SubscriptionStatus.Cancelled, SubscriptionStatus.Active],
            [SubscriptionStatus.GracePeriod] = [SubscriptionStatus.Active, SubscriptionStatus.Suspended,
                SubscriptionStatus.Cancelled, SubscriptionStatus.Expired],
            [SubscriptionStatus.Suspended] = [SubscriptionStatus.Active, SubscriptionStatus.Cancelled, SubscriptionStatus.Expired],
            [SubscriptionStatus.Expired] = [SubscriptionStatus.Active, SubscriptionStatus.Cancelled],
            [SubscriptionStatus.Cancelled] = [],
        };

    public static Result EnsureAllowed(SubscriptionStatus from, SubscriptionStatus to) =>
        Transitions.TryGetValue(from, out var allowed) && allowed.Contains(to)
            ? Result.Ok() : Result.Fail(ErrorCodes.SubscriptionInvalidTransition);
}
