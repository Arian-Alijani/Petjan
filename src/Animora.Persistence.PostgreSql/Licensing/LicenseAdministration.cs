using System.Globalization;
using System.Text.Json;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Subscriptions;
using Animora.Domain.Time;
using Animora.Persistence.PostgreSql.Operator;
using Animora.Persistence.PostgreSql.Outbox;
using Microsoft.EntityFrameworkCore;
namespace Animora.Persistence.PostgreSql.Licensing;

public sealed record LicenseListItem(string Id, string DeviceId, long Sequence, DateTimeOffset IssuedUtc,
    DateTimeOffset ExpiresUtc, LicenseRecordStatus Status, DateTimeOffset? RevokedUtc, string? RevokeReason);
public sealed class LicenseAdministration(LicenseStore db, IClock clock)
{
    public async Task<IReadOnlyList<LicenseListItem>> ListAsync(string tenantId, int page, int size, ActorRef actor,
        string correlationId, CancellationToken ct = default)
    {
        size = Math.Clamp(size, 1, 200); page = Math.Clamp(page, 1, int.MaxValue / 200);
        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var rows = await db.Licenses.AsNoTracking().Where(x => x.TenantId == tenantId).OrderByDescending(x => x.Id)
            .Skip((page - 1) * size).Take(size).Select(x => new LicenseListItem(x.Id, x.DeviceId, x.Sequence,
                x.IssuedUtc, x.ExpiresUtc, x.Status, x.RevokedUtc, x.RevokeReason)).ToListAsync(ct).ConfigureAwait(false);
        db.Audit.Add(OperatorAudit.Create(clock.UtcNow, actor, "license.list", tenantId, correlationId));
        await db.SaveChangesAsync(ct).ConfigureAwait(false); await tx.CommitAsync(ct).ConfigureAwait(false);
        return rows;
    }

    public async Task<Result> RevokeAsync(string tenantId, string licenseId, string expectedSequence, string reason,
        ActorRef actor, string correlationId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 200) return Result.Fail(ErrorCodes.ValidationFailed);
        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        // Same lock order as issuance: grant, then device licence. Activation cannot bypass revocation.
        var grant = await LockGrantAsync(tenantId, ct).ConfigureAwait(false);
        if (grant is null) return Result.Fail(ErrorCodes.LicenseNotGranted);
        var record = await db.Licenses.FromSqlInterpolated($"SELECT * FROM public.\"Licenses\" WHERE \"TenantId\"={tenantId} AND \"Id\"={licenseId} FOR UPDATE")
            .SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (record is null) return Result.Fail(ErrorCodes.NotFound);
        if (record.Sequence.ToString(CultureInfo.InvariantCulture) != expectedSequence.Trim('"')) return Result.Fail(ErrorCodes.ConcurrencyConflict);
        if (record.Status != LicenseRecordStatus.Revoked)
        {
            record.Status = LicenseRecordStatus.Revoked; record.RevokedUtc = clock.UtcNow;
            record.RevokedByActor = actor.ActorId; record.RevokeReason = reason.Trim();
        }
        // A free-text reason is stored only in the record, never duplicated into logs/audit payloads.
        db.Audit.Add(OperatorAudit.Create(clock.UtcNow, actor, "license.revoked", tenantId, correlationId,
            detail: new { licenseId, record.Sequence }));
        await db.SaveChangesAsync(ct).ConfigureAwait(false); await tx.CommitAsync(ct).ConfigureAwait(false);
        return Result.Ok();
    }

    public async Task<Result> RecheckAsync(string tenantId, string expectedSequence, ActorRef actor,
        string correlationId, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var grant = await LockGrantAsync(tenantId, ct).ConfigureAwait(false);
        if (grant is null) return Result.Fail(ErrorCodes.LicenseNotGranted);
        if (grant.Sequence.ToString(CultureInfo.InvariantCulture) != expectedSequence.Trim('"')) return Result.Fail(ErrorCodes.ConcurrencyConflict);
        var now = clock.UtcNow;
        db.Messages.Add(new OutboxMessage { TenantId = tenantId, Id = IdGenerator.NewId(now),
            MessageType = OutboxMessageTypes.LicenseRecheck, PayloadJson = JsonSerializer.Serialize(new { tenantId }),
            CorrelationId = correlationId, Status = OutboxStatus.Pending, CreatedUtc = now, NextAttemptUtc = now });
        db.Audit.Add(OperatorAudit.Create(now, actor, "license.recheck_requested", tenantId, correlationId));
        await db.SaveChangesAsync(ct).ConfigureAwait(false); await tx.CommitAsync(ct).ConfigureAwait(false);
        return Result.Ok();
    }
    private Task<LicenseGrant?> LockGrantAsync(string tenantId, CancellationToken ct) => db.Grants
        .FromSqlInterpolated($"SELECT * FROM public.\"LicenseGrants\" WHERE \"TenantId\"={tenantId} FOR UPDATE")
        .SingleOrDefaultAsync(ct);
}
