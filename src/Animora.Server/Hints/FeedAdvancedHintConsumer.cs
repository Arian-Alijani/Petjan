using System.Text.Json;
using Animora.Domain.Results;
using Animora.Persistence.PostgreSql.Outbox;
using Microsoft.AspNetCore.SignalR;
namespace Animora.Server.Hints;

internal sealed class FeedAdvancedHintConsumer(IHubContext<SyncHintHub> hub) : IOutboxConsumer
{
    public string MessageType => OutboxMessageTypes.FeedAdvanced;
    public string Name => "feed-hint";
    public async Task<Result> ConsumeAsync(OutboxMessage message, CancellationToken ct)
    {
        try
        {
            using var payload = JsonDocument.Parse(message.PayloadJson);
            var root = payload.RootElement;
            if (!root.TryGetProperty("tenantId", out var tenant) || tenant.GetString() != message.TenantId ||
                !root.TryGetProperty("lastSequence", out var sequence) || !sequence.TryGetInt64(out long position) || position < 1)
                return Result.Fail(ErrorCodes.ValidationFailed, "invalid_feed_hint");
            await hub.Clients.Group(SyncHintHub.Group(message.TenantId)).SendAsync("FeedAdvanced", position, ct).ConfigureAwait(false);
            return Result.Ok();
        }
        catch (JsonException) { return Result.Fail(ErrorCodes.ValidationFailed, "invalid_feed_hint"); }
    }
}
