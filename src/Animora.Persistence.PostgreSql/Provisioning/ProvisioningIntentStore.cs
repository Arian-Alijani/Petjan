using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Subscriptions;
using Animora.Persistence.PostgreSql.Operator;
using Microsoft.EntityFrameworkCore;
namespace Animora.Persistence.PostgreSql.Provisioning;

public sealed class ProvisioningIntentStore(ProvisioningDbContext db) : IProvisioningIntentStore
{
    public static bool IsPermanent(string code) => code is "TENANT_CODE_TAKEN" or "PLAN_NOT_FOUND" or "VALIDATION_FAILED"
        or "PROVISIONING_KEY_CONFLICT" or "PASSWORD_POLICY_VIOLATION";
    public async Task<Result<IntentReceipt>> InsertAsync(ProvisioningIntent row, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public."ProvisioningIntents" ("Id","IdempotencyKey","ClientId","RequestFingerprint","PayloadProtected",
                "Status","Attempts","NextAttemptUtc","ReceivedUtc")
            VALUES ({row.Id},{row.IdempotencyKey},{row.ClientId},{row.RequestFingerprint},{row.PayloadProtected},0,0,{row.NextAttemptUtc},{row.ReceivedUtc})
            ON CONFLICT ("IdempotencyKey") DO NOTHING
            """, ct).ConfigureAwait(false);
        var stored = await db.Intents.AsNoTracking().Where(x => x.IdempotencyKey == row.IdempotencyKey)
            .Select(x => new { x.Id, x.ClientId, x.RequestFingerprint, x.Status }).SingleAsync(ct).ConfigureAwait(false);
        if (stored.ClientId != row.ClientId || stored.RequestFingerprint != row.RequestFingerprint)
            return Result<IntentReceipt>.Fail(ErrorCodes.ProvisioningKeyConflict);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return Result<IntentReceipt>.Ok(new(stored.Id, stored.Status));
    }
    public async Task<Result<IntentPoll>> PollAsync(string key, string clientId, DateTimeOffset now, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var row = await db.Intents.FromSqlInterpolated($"SELECT * FROM public.\"ProvisioningIntents\" WHERE \"IdempotencyKey\"={key} AND \"ClientId\"={clientId} FOR UPDATE")
            .SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (row is null) return Result<IntentPoll>.Fail(ErrorCodes.ProvisioningIntentNotFound);
        var password = row.Status == ProvisioningIntentStatus.Completed && row.TempPasswordExpiresUtc > now ? row.ProtectedTempPassword : null;
        if (row.ProtectedTempPassword != null) { row.ProtectedTempPassword = null; row.TempPasswordExpiresUtc = null; }
        var result = new IntentPoll(row.Status, row.ResultJson, password, row.ErrorCode);
        await db.SaveChangesAsync(ct).ConfigureAwait(false); await tx.CommitAsync(ct).ConfigureAwait(false); db.ChangeTracker.Clear();
        return Result<IntentPoll>.Ok(result);
    }
    public async Task<IReadOnlyList<ProvisioningIntent>> ClaimAsync(DateTimeOffset now, int limit = 5, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var take = Math.Clamp(limit, 1, 5);
        var rows = await db.Intents.FromSqlInterpolated($"""
            SELECT * FROM public."ProvisioningIntents" WHERE ("Status"=0 AND "NextAttemptUtc"<={now})
              OR ("Status"=1 AND "ClaimedUntilUtc"<={now})
            ORDER BY "NextAttemptUtc", "Id" LIMIT {take} FOR UPDATE SKIP LOCKED
            """).ToListAsync(ct).ConfigureAwait(false);
        foreach (var row in rows)
        { row.Status = ProvisioningIntentStatus.Processing; row.Attempts++; row.ClaimToken = IdGenerator.NewId(now); row.ClaimedUntilUtc = now.AddSeconds(60); }
        await db.SaveChangesAsync(ct).ConfigureAwait(false); await tx.CommitAsync(ct).ConfigureAwait(false); db.ChangeTracker.Clear();
        return rows;
    }
    public async Task<bool> CompleteAsync(ProvisioningIntent claim, string resultJson, byte[]? protectedPassword, DateTimeOffset now, CancellationToken ct = default)
    {
        var expires = protectedPassword is null ? (DateTimeOffset?)null : now.AddHours(72);
        var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE public."ProvisioningIntents" SET "Status"=2,"ResultJson"={resultJson}::jsonb,"ProtectedTempPassword"={protectedPassword},
                "TempPasswordExpiresUtc"={expires},"CompletedUtc"={now},"ClaimToken"=NULL,"ClaimedUntilUtc"=NULL,"ErrorCode"=NULL
            WHERE "Id"={claim.Id} AND "Status"=1 AND "ClaimToken"={claim.ClaimToken} AND "ClaimedUntilUtc">{now}
            """, ct).ConfigureAwait(false);
        return changed == 1;
    }
    public async Task<bool> FailAsync(ProvisioningIntent claim, string errorCode, bool permanent, TimeSpan retryDelay, DateTimeOffset now, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var status = permanent ? ProvisioningIntentStatus.Failed : claim.Attempts >= 8 ? ProvisioningIntentStatus.Dead : ProvisioningIntentStatus.Received;
        var next = now + retryDelay;
        var terminal = status is ProvisioningIntentStatus.Dead or ProvisioningIntentStatus.Failed ? now : (DateTimeOffset?)null;
        var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE public."ProvisioningIntents" SET "Status"={(short)status},"ErrorCode"={errorCode},"NextAttemptUtc"={next},
              "CompletedUtc"={terminal},"ClaimToken"=NULL,"ClaimedUntilUtc"=NULL
            WHERE "Id"={claim.Id} AND "Status"=1 AND "ClaimToken"={claim.ClaimToken} AND "ClaimedUntilUtc">{now}
            """, ct).ConfigureAwait(false);
        if (changed != 0 && status == ProvisioningIntentStatus.Dead)
        { db.Audit.Add(OperatorAudit.Create(now, new ActorRef(ActorKind.System), "provisioning.intent.dead", null, claim.Id, "failure", new { errorCode })); await db.SaveChangesAsync(ct).ConfigureAwait(false); }
        await tx.CommitAsync(ct).ConfigureAwait(false); return changed == 1;
    }
    public async Task<int> RetainAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var wipeAt = now.AddDays(-7); var deleteAt = now.AddDays(-400);
        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
            WITH due AS (SELECT "Id" FROM public."ProvisioningIntents" WHERE
              ("ReceivedUtc"<{wipeAt} AND "PayloadProtected" IS NOT NULL) OR
              ("TempPasswordExpiresUtc"<={now} AND "ProtectedTempPassword" IS NOT NULL)
              ORDER BY "ReceivedUtc" LIMIT 500 FOR UPDATE SKIP LOCKED)
            UPDATE public."ProvisioningIntents" i SET "PayloadProtected"=CASE WHEN i."ReceivedUtc"<{wipeAt} THEN NULL ELSE i."PayloadProtected" END,
              "ProtectedTempPassword"=CASE WHEN i."TempPasswordExpiresUtc"<={now} THEN NULL ELSE i."ProtectedTempPassword" END,
              "TempPasswordExpiresUtc"=CASE WHEN i."TempPasswordExpiresUtc"<={now} THEN NULL ELSE i."TempPasswordExpiresUtc" END
            FROM due WHERE i."Id"=due."Id"
            """, ct).ConfigureAwait(false);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM public."ProvisioningIntents" WHERE "Id" IN (SELECT "Id" FROM public."ProvisioningIntents"
                WHERE "ReceivedUtc"<{deleteAt} AND "Status" IN (2,3,4) ORDER BY "ReceivedUtc" LIMIT 500)
            """, ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false); return changed;
    }
    public async Task<IReadOnlyList<IntentListItem>> ListAsync(ProvisioningIntentStatus? status, int page, int size, CancellationToken ct = default) =>
        await db.Intents.AsNoTracking().Where(x => status == null || x.Status == status).OrderByDescending(x => x.ReceivedUtc).ThenBy(x => x.Id)
            .Skip((Math.Clamp(page, 1, int.MaxValue / 200) - 1) * Math.Clamp(size, 1, 200)).Take(Math.Clamp(size, 1, 200))
            .Select(x => new IntentListItem(x.Id, x.IdempotencyKey, x.ClientId, x.Status, x.Attempts, x.ErrorCode, x.ReceivedUtc, x.CompletedUtc))
            .ToListAsync(ct).ConfigureAwait(false);
    public async Task<Result> RetryAsync(string id, ActorRef actor, DateTimeOffset now, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var row = await db.Intents.FromSqlInterpolated($"SELECT * FROM public.\"ProvisioningIntents\" WHERE \"Id\"={id} FOR UPDATE")
            .SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (row is null) return Result.Fail(ErrorCodes.ProvisioningIntentNotFound);
        if (row.Status is not (ProvisioningIntentStatus.Dead or ProvisioningIntentStatus.Failed) || row.PayloadProtected is null || IsPermanent(row.ErrorCode ?? string.Empty))
            return Result.Fail(ErrorCodes.ValidationFailed, "intent_not_retryable");
        row.Status = ProvisioningIntentStatus.Received; row.Attempts = 0; row.NextAttemptUtc = now; row.ErrorCode = null;
        row.CompletedUtc = null; row.ClaimToken = null; row.ClaimedUntilUtc = null;
        db.Audit.Add(OperatorAudit.Create(now, actor, "provisioning.intent.retry", null, row.Id));
        await db.SaveChangesAsync(ct).ConfigureAwait(false); await tx.CommitAsync(ct).ConfigureAwait(false); return Result.Ok();
    }
}
