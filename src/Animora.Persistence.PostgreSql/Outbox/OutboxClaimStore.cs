using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Microsoft.EntityFrameworkCore;
namespace Animora.Persistence.PostgreSql.Outbox;

public sealed class OutboxClaimStore(OutboxDbContext context)
{
    public static readonly TimeSpan Lease = TimeSpan.FromSeconds(60);
    public const string ClaimSql = """
        WITH due AS (
          SELECT "TenantId", "Id" FROM public."ServerOutbox"
          WHERE ("Status"=0 AND "NextAttemptUtc"<=@now) OR ("Status"=1 AND "ClaimedUntilUtc"<=@now)
          ORDER BY "NextAttemptUtc", "TenantId", "Id" LIMIT @n FOR UPDATE SKIP LOCKED)
        UPDATE public."ServerOutbox" o SET "Status"=1, "ClaimToken"=@token,
          "ClaimedUntilUtc"=@now+@lease, "Attempts"=o."Attempts"+1
          FROM due WHERE o."TenantId"=due."TenantId" AND o."Id"=due."Id" RETURNING o.*
        """;
    public async Task<IReadOnlyList<OutboxMessage>> ClaimAsync(DateTimeOffset now, int count, CancellationToken ct)
    {
        string token = IdGenerator.NewId(now);
        var parameters = new object[] { new Npgsql.NpgsqlParameter("now", now), new Npgsql.NpgsqlParameter("n", Math.Clamp(count, 1, 20)),
            new Npgsql.NpgsqlParameter("token", token), new Npgsql.NpgsqlParameter("lease", Lease) };
        // No composition over UPDATE RETURNING, and never a tenant filter bypass.
        return await context.Messages.FromSqlRaw(ClaimSql, parameters).AsNoTracking().ToArrayAsync(ct).ConfigureAwait(false);
    }
    public Task<int> CompleteAsync(OutboxMessage m, DateTimeOffset now, CancellationToken ct) =>
        context.Messages.Where(x => x.TenantId == m.TenantId && x.Id == m.Id && x.Status == OutboxStatus.Claimed &&
            x.ClaimToken == m.ClaimToken && x.ClaimedUntilUtc > now).ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, OutboxStatus.Done).SetProperty(x => x.CompletedUtc, now)
                .SetProperty(x => x.ClaimToken, (string?)null).SetProperty(x => x.ClaimedUntilUtc, (DateTimeOffset?)null), ct);
    public async Task<int> FailAsync(OutboxMessage m, DateTimeOffset now, TimeSpan delay, bool dead, string code, CancellationToken ct)
    {
        await using var tx = await context.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        string safeCode = code.Length > 512 ? code[..512] : code;
        int updated = await context.Messages.Where(x => x.TenantId == m.TenantId && x.Id == m.Id && x.Status == OutboxStatus.Claimed &&
            x.ClaimToken == m.ClaimToken && x.ClaimedUntilUtc > now).ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, dead ? OutboxStatus.DeadLettered : OutboxStatus.Pending)
                .SetProperty(x => x.NextAttemptUtc, now + delay).SetProperty(x => x.LastError, safeCode)
                .SetProperty(x => x.CompletedUtc, dead ? now : (DateTimeOffset?)null)
                .SetProperty(x => x.ClaimToken, (string?)null).SetProperty(x => x.ClaimedUntilUtc, (DateTimeOffset?)null), ct).ConfigureAwait(false);
        if (updated == 1 && dead)
        {
            var id = IdGenerator.NewId(now);
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO public."SecurityAudit" ("Id","TenantId","OccurredUtc","Category","Action","Outcome","CorrelationId")
                VALUES ({id},{m.TenantId},{now},'outbox',{ErrorCodes.OutboxDeadLettered.Code},'failure',{m.CorrelationId})
                """, ct).ConfigureAwait(false);
        }
        await tx.CommitAsync(ct).ConfigureAwait(false); return updated;
    }
    public Task<int> CleanupAsync(DateTimeOffset cutoff, CancellationToken ct) =>
        context.Messages.Where(m => m.Status == OutboxStatus.Done && m.CompletedUtc < cutoff).ExecuteDeleteAsync(ct);
}
