using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Animora.Domain.Primitives;
using Animora.Domain.Subscriptions;
using Animora.Domain.Time;
using Animora.Persistence.PostgreSql.Operator;
using Animora.Persistence.PostgreSql.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Subscriptions;

public sealed class SubscriptionSweep(AnimoraOperatorDbContext db, IClock clock)
{
    public async Task<int> EnqueueDueAsync(CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var now = clock.UtcNow;
        var rows = await db.Subscriptions.FromSqlInterpolated($"""
            SELECT * FROM public."Subscriptions"
            WHERE ("Status" IN (0,1) AND "CurrentPeriodEndUtc"<={now})
               OR ("Status"=2 AND ("GraceEndsUtc"<={now} OR ("CancelAtPeriodEnd" AND "CurrentPeriodEndUtc"<={now})))
            ORDER BY "CurrentPeriodEndUtc", "Id" LIMIT 200 FOR UPDATE SKIP LOCKED
            """).AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
        var added = 0;
        foreach (var s in rows)
        {
            var target = s.Status == SubscriptionStatus.GracePeriod && !s.CancelAtPeriodEnd ? s.GraceEndsUtc ?? s.CurrentPeriodEndUtc : s.CurrentPeriodEndUtc;
            var key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(s.Id + ":" + target.ToUnixTimeMilliseconds())));
            if (await db.Messages.AsNoTracking().AnyAsync(m => m.TenantId == s.TenantId &&
                m.MessageType == OutboxMessageTypes.SubscriptionExpiry && m.CorrelationId == key, ct).ConfigureAwait(false)) continue;
            db.Messages.Add(new OutboxMessage { TenantId = s.TenantId, Id = IdGenerator.NewId(now),
                MessageType = OutboxMessageTypes.SubscriptionExpiry, PayloadJson = JsonSerializer.Serialize(new { subscriptionId = s.Id }),
                Status = OutboxStatus.Pending, CreatedUtc = now, NextAttemptUtc = now, CorrelationId = key });
            added++;
        }
        await db.SaveChangesAsync(ct).ConfigureAwait(false); await tx.CommitAsync(ct).ConfigureAwait(false);
        return added;
    }
}
