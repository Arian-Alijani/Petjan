using Animora.Domain.Results;
using Animora.Domain.Subscriptions;

namespace Animora.Persistence.PostgreSql.Subscriptions;

public sealed record SubscriptionMutation(string Operation, int? Days = null, DateTimeOffset? UntilUtc = null,
    string? Reason = null, bool Immediate = true, int? Seats = null, string? PlanCode = null);
public interface ISubscriptionService
{
    Task<Result<Subscription>> MutateAsync(string tenantId, string expectedStamp, SubscriptionMutation mutation,
        ActorRef actor, CancellationToken ct = default);
}
