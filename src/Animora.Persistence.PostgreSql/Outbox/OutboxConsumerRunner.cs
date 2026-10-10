using Animora.Domain.Results;
using Microsoft.EntityFrameworkCore;
namespace Animora.Persistence.PostgreSql.Outbox;

/// <summary>Fenced consumed-marker and effect share a transaction; duplicate delivery becomes a successful no-op.</summary>
public sealed class OutboxConsumerRunner(OutboxDbContext context)
{
    public async Task<Result> RunAsync(IOutboxConsumer consumer, OutboxMessage m, DateTimeOffset now, CancellationToken ct)
    {
        await using var tx = await context.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var fenced = await context.Messages.FromSqlInterpolated($"""
            SELECT * FROM public."ServerOutbox" WHERE "TenantId"={m.TenantId} AND "Id"={m.Id}
              AND "Status"=1 AND "ClaimToken"={m.ClaimToken} AND "ClaimedUntilUtc">{now} FOR UPDATE
            """).AsNoTracking().SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (fenced is null) return Result.Fail(ErrorCodes.CommandClaimLost);
        int inserted = await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public."ConsumedMessages" ("ConsumerName","TenantId","MessageId","ConsumedUtc")
            VALUES ({consumer.Name},{m.TenantId},{m.Id},{now})
            ON CONFLICT ("ConsumerName","TenantId","MessageId") DO NOTHING
            """, ct).ConfigureAwait(false);
        if (inserted == 0) { await tx.CommitAsync(ct).ConfigureAwait(false); return Result.Ok(); }
        Result effect = await consumer.ConsumeAsync(m, ct).ConfigureAwait(false);
        if (effect.IsFailure) return effect; // Dispose rolls back marker as well as the consumer's DB effect.
        await tx.CommitAsync(ct).ConfigureAwait(false); return Result.Ok();
    }
}
