using Animora.Domain.Results;
using Animora.Domain.Subscriptions;
namespace Animora.Persistence.PostgreSql.Provisioning;

public sealed record IntentReceipt(string IntentId, ProvisioningIntentStatus Status);
public sealed record IntentPoll(ProvisioningIntentStatus Status, string? ResultJson, byte[]? ProtectedTempPassword, string? ErrorCode);
public sealed record IntentListItem(string Id, string IdempotencyKey, string ClientId, ProvisioningIntentStatus Status,
    int Attempts, string? ErrorCode, DateTimeOffset ReceivedUtc, DateTimeOffset? CompletedUtc);
public interface IProvisioningIntentStore
{
    Task<Result<IntentReceipt>> InsertAsync(ProvisioningIntent intent, CancellationToken ct = default);
    Task<Result<IntentPoll>> PollAsync(string key, string clientId, DateTimeOffset now, CancellationToken ct = default);
    Task<IReadOnlyList<ProvisioningIntent>> ClaimAsync(DateTimeOffset now, int limit = 5, CancellationToken ct = default);
    Task<bool> CompleteAsync(ProvisioningIntent claim, string resultJson, byte[]? protectedPassword, DateTimeOffset now, CancellationToken ct = default);
    Task<bool> FailAsync(ProvisioningIntent claim, string errorCode, bool permanent, TimeSpan retryDelay, DateTimeOffset now, CancellationToken ct = default);
    Task<int> RetainAsync(DateTimeOffset now, CancellationToken ct = default);
    Task<IReadOnlyList<IntentListItem>> ListAsync(ProvisioningIntentStatus? status, int page, int size, CancellationToken ct = default);
    Task<Result> RetryAsync(string id, ActorRef actor, DateTimeOffset now, CancellationToken ct = default);
}
