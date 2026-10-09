using Animora.Domain.Results;
using Animora.Persistence.PostgreSql.Context;
using Animora.Persistence.PostgreSql.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Commands;

/// <inheritdoc cref="ICommandIntakeStore" />
public sealed class CommandIntakeStore(AnimoraServerDbContext context, ITenantContext tenantContext) : ICommandIntakeStore
{
    private const int MaxDetailLength = 512;

    /// <inheritdoc />
    public async Task<IntakeBeginResult> TryBeginAsync(CommandIntakeRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        string tenantId = RequireTenant();
        if (!string.Equals(record.TenantId, tenantId, StringComparison.Ordinal))
        {
            throw new TenantMismatchException(tenantId, record.TenantId);
        }

        short status = (short)record.Status;
        int inserted = await context.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO public."CommandIntake"
                ("TenantId", "OperationId", "CommandType", "Fingerprint", "Status", "FinancialEffect", "ReceivedUtc",
                 "AttemptId", "LeaseOwner", "LeaseUntilUtc", "AttemptCount", "PayloadJson", "DependsOnOperationId", "CorrelationId")
            VALUES
                ({record.TenantId}, {record.OperationId}, {record.CommandType}, {record.Fingerprint}, {status}, {record.FinancialEffect}, {record.ReceivedUtc},
                 {record.AttemptId}, {record.LeaseOwner}, {record.LeaseUntilUtc}, {record.AttemptCount}, {record.PayloadJson}::jsonb, {record.DependsOnOperationId}, {record.CorrelationId})
            ON CONFLICT ("TenantId", "OperationId") DO NOTHING
            """,
            ct).ConfigureAwait(false);

        if (inserted == 1)
        {
            return new IntakeBeginResult(true, null);
        }

        CommandIntakeRecord? existing = await FindAsync(record.OperationId, ct).ConfigureAwait(false);
        return new IntakeBeginResult(false, existing);
    }

    /// <inheritdoc />
    public async Task<CommandIntakeRecord?> FindAsync(string operationId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        string tenantId = RequireTenant();

        return await context.CommandIntake
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.TenantId == tenantId && e.OperationId == operationId, ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result> CompleteAsync(IntakeSettlement settlement, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        string tenantId = RequireTenant();
        string? detail = Truncate(settlement.Detail);
        short settled = (short)IntakeStatus.Settled;
        short inProgress = (short)IntakeStatus.InProgress;

        int updated = await context.Database.ExecuteSqlAsync(
            $"""
            UPDATE public."CommandIntake"
               SET "Status" = {settled}, "Outcome" = {settlement.Outcome}, "ErrorCode" = {settlement.ErrorCode}, "Detail" = {detail},
                   "ServerEntityId" = {settlement.ServerEntityId}, "ServerVersion" = {settlement.ServerVersion},
                   "FinancialEffect" = {settlement.FinancialEffect}, "ResponsibleRole" = {settlement.ResponsibleRole},
                   "SettledUtc" = {settlement.SettledUtc.ToUniversalTime()}, "LeaseUntilUtc" = NULL, "LeaseOwner" = NULL
             WHERE "TenantId" = {tenantId} AND "OperationId" = {settlement.OperationId}
               AND "Status" = {inProgress} AND "AttemptId" = {settlement.AttemptId}
            """,
            ct).ConfigureAwait(false);

        if (updated != 1)
        {
            return Result.Fail(ErrorCodes.ConcurrencyConflict, "intake_fenced");
        }

        foreach (CommandIdMapItem item in settlement.Ids)
        {
            // Idempotent per (tenant, operation, slot): a re-run of the same attempt cannot duplicate a slot.
            await context.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO public."CommandIdMap" ("TenantId", "OperationId", "Slot", "EntityType", "ServerEntityId")
                VALUES ({tenantId}, {settlement.OperationId}, {item.Slot}, {item.EntityType}, {item.ServerEntityId})
                ON CONFLICT ("TenantId", "OperationId", "Slot") DO NOTHING
                """,
                ct).ConfigureAwait(false);
        }

        return Result.Ok();
    }

    /// <inheritdoc />
    public async Task<bool> RenewLeaseAsync(string operationId, string attemptId, DateTimeOffset leaseUntilUtc, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(attemptId);
        string tenantId = RequireTenant();
        short inProgress = (short)IntakeStatus.InProgress;
        DateTimeOffset until = leaseUntilUtc.ToUniversalTime();

        int updated = await context.Database.ExecuteSqlAsync(
            $"""
            UPDATE public."CommandIntake" SET "LeaseUntilUtc" = {until}
             WHERE "TenantId" = {tenantId} AND "OperationId" = {operationId}
               AND "Status" = {inProgress} AND "AttemptId" = {attemptId}
            """,
            ct).ConfigureAwait(false);
        return updated == 1;
    }

    /// <inheritdoc />
    public async Task<bool> TryAcquireAttemptAsync(
        string operationId,
        string? observedAttemptId,
        string newAttemptId,
        string leaseOwner,
        DateTimeOffset nowUtc,
        DateTimeOffset leaseUntilUtc,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(newAttemptId);
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseOwner);
        string tenantId = RequireTenant();
        short inProgress = (short)IntakeStatus.InProgress;
        short failed = (short)IntakeStatus.Failed;
        DateTimeOffset now = nowUtc.ToUniversalTime();
        DateTimeOffset until = leaseUntilUtc.ToUniversalTime();

        // Compare-and-set: only the instance that still sees the observed attempt (and an expired/failed row) wins.
        int updated = await context.Database.ExecuteSqlAsync(
            $"""
            UPDATE public."CommandIntake"
               SET "Status" = {inProgress}, "AttemptId" = {newAttemptId}, "LeaseOwner" = {leaseOwner},
                   "LeaseUntilUtc" = {until}, "AttemptCount" = "AttemptCount" + 1
             WHERE "TenantId" = {tenantId} AND "OperationId" = {operationId}
               AND "AttemptId" IS NOT DISTINCT FROM {observedAttemptId}
               AND (("Status" = {inProgress} AND "LeaseUntilUtc" < {now}) OR "Status" = {failed})
            """,
            ct).ConfigureAwait(false);
        return updated == 1;
    }

    /// <inheritdoc />
    public async Task<bool> MarkFailedAsync(string operationId, string observedAttemptId, string? detail, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(observedAttemptId);
        string tenantId = RequireTenant();
        short inProgress = (short)IntakeStatus.InProgress;
        short failed = (short)IntakeStatus.Failed;
        string? text = Truncate(detail);

        int updated = await context.Database.ExecuteSqlAsync(
            $"""
            UPDATE public."CommandIntake"
               SET "Status" = {failed}, "Detail" = {text}, "LeaseUntilUtc" = NULL, "LeaseOwner" = NULL
             WHERE "TenantId" = {tenantId} AND "OperationId" = {operationId}
               AND "Status" = {inProgress} AND "AttemptId" = {observedAttemptId}
            """,
            ct).ConfigureAwait(false);
        return updated == 1;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CommandIntakeRecord>> ListExpiredLeasesAsync(DateTimeOffset nowUtc, int limit, CancellationToken ct = default)
    {
        string tenantId = RequireTenant();
        int take = Math.Clamp(limit, 1, ICommandIntakeStore.MaxRecoveryBatch);
        DateTimeOffset now = nowUtc.ToUniversalTime();

        return await context.CommandIntake
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.Status == IntakeStatus.InProgress && e.LeaseUntilUtc < now)
            .OrderBy(e => e.LeaseUntilUtc)
            .Take(take)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CommandIdMapItem>> GetIdMapAsync(string operationId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        string tenantId = RequireTenant();

        return await context.CommandIdMap
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.OperationId == operationId)
            .OrderBy(e => e.Slot)
            .Select(e => new CommandIdMapItem(e.Slot, e.EntityType, e.ServerEntityId))
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    private string RequireTenant() => tenantContext.TenantId ?? throw new TenantContextMissingException();

    private static string? Truncate(string? value) =>
        value is { Length: > MaxDetailLength } ? value[..MaxDetailLength] : value;
}
