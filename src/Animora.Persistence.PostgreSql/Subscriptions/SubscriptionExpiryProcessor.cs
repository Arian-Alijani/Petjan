using System.Text.Json;
using Animora.Domain.Results;
using Animora.Domain.Subscriptions;
using Animora.Persistence.PostgreSql.Operator;
using Animora.Persistence.PostgreSql.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Animora.Persistence.PostgreSql.Subscriptions;

/// <summary>Enlists operator effects in the relay's fenced transaction; marker and effect commit together.</summary>
public sealed class SubscriptionExpiryProcessor(AnimoraOperatorDbContext db, OutboxDbContext relay, ISubscriptionService service)
{
    public async Task<Result> ConsumeAsync(OutboxMessage message, CancellationToken ct = default)
    {
        string? subscriptionId;
        try { using var payload = JsonDocument.Parse(message.PayloadJson); subscriptionId = payload.RootElement.GetProperty("subscriptionId").GetString(); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        { return Result.Fail(ErrorCodes.ValidationFailed); }
        var tx = relay.Database.CurrentTransaction ?? throw new InvalidOperationException("Consumer requires the fenced relay transaction.");
        db.Database.SetDbConnection(relay.Database.GetDbConnection());
        await db.Database.UseTransactionAsync(tx.GetDbTransaction(), ct).ConfigureAwait(false);
        var s = await db.Subscriptions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == subscriptionId && x.TenantId == message.TenantId, ct).ConfigureAwait(false);
        if (s is null) return Result.Ok();
        var result = await service.MutateAsync(message.TenantId, s.ConcurrencyStamp, new SubscriptionMutation("tick"),
            new ActorRef(ActorKind.System), ct).ConfigureAwait(false);
        return result.ToResult();
    }
}
