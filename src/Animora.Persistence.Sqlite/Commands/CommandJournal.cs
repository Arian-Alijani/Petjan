using Animora.Application.Abstractions;
using Animora.Application.Commands;
using Animora.Application.UseCases.Sync;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Animora.Persistence.Sqlite.Options;
using Animora.Persistence.Sqlite.Queries;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Animora.Persistence.Sqlite.Commands;

/// <summary>
/// The durable, idempotent command journal (ARCHITECTURE.md §8.3/§12.1). <see cref="EnqueueAsync"/> only
/// <c>Add</c>s — it never calls <c>SaveChanges</c>, so it rides the caller's business transaction and the
/// command and its business change commit or roll back together.
/// </summary>
public sealed class CommandJournal(
    AnimoraLocalDbContext db,
    IClock clock,
    IIntegrityAlertSink integrity,
    IOptions<LocalStoreOptions> options) : ICommandJournal
{
    public async Task<Result<PendingCommandRecord>> EnqueueAsync(CommandEnvelope envelope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var existing = await db.Set<PendingCommand>().AsNoTracking()
            .FirstOrDefaultAsync(c => c.OperationId == envelope.OperationId, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            // A byte-identical retry is a replay: return the stored record, never a second row (A-RULE 005/006).
            if (string.Equals(existing.CommandFingerprint, envelope.CommandFingerprint, StringComparison.Ordinal))
                return Result<PendingCommandRecord>.Ok(Map(existing));

            // Same OperationId, different payload: a security/integrity event, audited, never a replay (A-RULE 019).
            var detail =
                $"OperationId={envelope.OperationId} stored={existing.CommandFingerprint} incoming={envelope.CommandFingerprint}";
            var alerted = await integrity.RaiseAsync(new IntegrityAlert(
                ErrorCodes.IdempotencyFingerprintMismatch.Code,
                detail,
                envelope.TenantId,
                envelope.UserId,
                envelope.DeviceId,
                envelope.OperationId,
                envelope.AggregateType,
                envelope.AggregateId), ct).ConfigureAwait(false);
            return alerted.IsFailure
                ? Result<PendingCommandRecord>.Fail(ErrorCodes.AuditWriteFailed, alerted.Detail ?? alerted.Error.Code)
                : Result<PendingCommandRecord>.Fail(ErrorCodes.IdempotencyFingerprintMismatch,
                    $"OperationId={envelope.OperationId}");
        }

        var row = PendingCommand.Create(envelope, clock.UtcNow);
        db.Add(row); // No SaveChanges: the caller's unit of work commits this with the business write.
        return Result<PendingCommandRecord>.Ok(Map(row));
    }

    public async Task<Result<PendingCommandRecord?>> FindByOperationIdAsync(string operationId, CancellationToken ct = default)
    {
        var row = await db.Set<PendingCommand>().AsNoTracking()
            .FirstOrDefaultAsync(c => c.OperationId == operationId, ct).ConfigureAwait(false);
        return Result<PendingCommandRecord?>.Ok(row is null ? null : Map(row));
    }

    public async Task<Result> ApplySettlementAsync(SettlementResult result, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        var row = await db.Set<PendingCommand>().AsTracking()
            .FirstOrDefaultAsync(c => c.OperationId == result.OperationId, ct).ConfigureAwait(false);
        if (row is null) return Result.Fail(ErrorCodes.NotFound, $"OperationId={result.OperationId}");

        var target = TargetState(result.Outcome);
        var allowed = SettlementTransitions.EnsureAllowed(row.Status, target);
        if (allowed.IsFailure) return allowed;

        row.ApplySettlement(target, result, clock.UtcNow);
        await ReconcileInvoiceAsync(row, target, result, ct).ConfigureAwait(false);
        return await SaveAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// D05: settlement must reach the business aggregate, not only the journal. For an invoice-scoped command
    /// the local invoice's settlement verdict is updated in the same save: a refusal flags it
    /// <c>Rejected</c>/<c>Conflict</c> with the server's code (non-destructive — posted money/stock is never
    /// undone locally, ARCH §8.5 reversal-only; the operator acts on the flag); an acceptance marks it
    /// <c>Settled</c> once no other command on the invoice is still unsettled, and never clears an earlier,
    /// unresolved refusal. The server's invoice number cannot be rehydrated here — the settlement contract
    /// does not carry it — so the local number stays provisional until a server-authored read replaces it.
    /// </summary>
    private async Task ReconcileInvoiceAsync(PendingCommand row, SettlementState target, SettlementResult result,
        CancellationToken ct)
    {
        if (!string.Equals(row.AggregateType, "Invoice", StringComparison.Ordinal)) return;
        // Invoice creations have no aggregate id yet; their invoice id IS the operation id.
        var invoiceId = row.AggregateId ?? row.OperationId;
        var invoice = await db.Set<Animora.Domain.Sales.Invoice>().AsTracking()
            .FirstOrDefaultAsync(i => i.Id == invoiceId, ct).ConfigureAwait(false);
        if (invoice is null) return;

        switch (target)
        {
            case SettlementState.Rejected:
                // SYN-03: a dependent refused only because its prerequisite was refused (DEPENDENCY_REJECTED) must not overwrite
                // the prerequisite's own verdict — the operator has to see the ROOT failure (e.g. the rejected invoice post), not
                // the derived one. A real refusal of the dependent itself still replaces a non-terminal/other verdict.
                if (IsDerivedRefusal(result) && invoice.Settlement is Animora.Domain.Sales.SettlementStatus.Rejected
                        or Animora.Domain.Sales.SettlementStatus.Conflict) return;
                invoice.MarkSettlement(Animora.Domain.Sales.SettlementStatus.Rejected, result.ErrorCode);
                break;
            case SettlementState.Conflict:
                invoice.MarkSettlement(Animora.Domain.Sales.SettlementStatus.Conflict, result.ErrorCode);
                break;
            case SettlementState.Settled:
                if (invoice.State == Animora.Domain.Sales.InvoiceState.Draft
                    || invoice.Settlement is Animora.Domain.Sales.SettlementStatus.Rejected
                        or Animora.Domain.Sales.SettlementStatus.Conflict) return;
                var others = UnsettledStates();
                var pending = await db.Set<PendingCommand>().AsNoTracking()
                    .AnyAsync(c => c.OperationId != row.OperationId && others.Contains(c.Status)
                                   && (c.AggregateId == invoiceId || c.OperationId == invoiceId), ct)
                    .ConfigureAwait(false);
                invoice.MarkSettlement(pending
                    ? Animora.Domain.Sales.SettlementStatus.PendingSettlement
                    : Animora.Domain.Sales.SettlementStatus.Settled);
                break;
        }
    }

    private static bool IsDerivedRefusal(SettlementResult result) =>
        string.Equals(result.ErrorCode, ErrorCodes.DependencyRejected.Code, StringComparison.Ordinal);

    public async Task<Result<IReadOnlyList<PendingCommandRecord>>> ClaimBatchAsync(int max, TimeSpan lease,
        CancellationToken ct = default)
    {
        if (max <= 0) return Result<IReadOnlyList<PendingCommandRecord>>.Ok([]);
        var now = clock.UtcNow;
        var token = IdGenerator.NewId(now);
        try
        {
            await PipelineSql.ClaimPendingCommandsAsync(db, token, now, now + lease, max, ct).ConfigureAwait(false);
            var rows = await db.Set<PendingCommand>().AsNoTracking()
                .Where(c => c.ClaimToken == token)
                .OrderBy(c => c.LocalSequence)
                .ToListAsync(ct).ConfigureAwait(false);
            IReadOnlyList<PendingCommandRecord> records = rows.Select(Map).ToArray();
            return Result<IReadOnlyList<PendingCommandRecord>>.Ok(records);
        }
        catch (SqliteException) { return Result<IReadOnlyList<PendingCommandRecord>>.Fail(ErrorCodes.StoreUnavailable); }
    }

    public async Task<Result> ReleaseAsync(string operationId, ErrorCode error, TimeSpan backoff,
        CancellationToken ct = default)
    {
        var row = await db.Set<PendingCommand>().AsTracking()
            .FirstOrDefaultAsync(c => c.OperationId == operationId, ct).ConfigureAwait(false);
        if (row is null) return Result.Fail(ErrorCodes.NotFound, $"OperationId={operationId}");

        // A deferral (offline or an unsettled dependency) never consumes the retry budget — only a real
        // failure can move the row to NeedsAttention.
        var isFailure = !error.IsNone && error != ErrorCodes.DependencyUnsettled;
        row.Release(error, backoff, clock.UtcNow, isFailure);
        return await SaveAsync(ct).ConfigureAwait(false);
    }

    public async Task<Result<IReadOnlyList<PendingCommandRecord>>> GetUnsettledForAggregateAsync(string aggregateType,
        string aggregateId, CancellationToken ct = default)
    {
        var unsettled = UnsettledStates();
        var rows = await db.Set<PendingCommand>().AsNoTracking()
            .Where(c => c.AggregateType == aggregateType && c.AggregateId == aggregateId
                        && unsettled.Contains(c.Status))
            .OrderBy(c => c.LocalSequence)
            .ToListAsync(ct).ConfigureAwait(false);
        IReadOnlyList<PendingCommandRecord> records = rows.Select(Map).ToArray();
        return Result<IReadOnlyList<PendingCommandRecord>>.Ok(records);
    }

    public async Task<Result<PendingCommandSummary>> GetSummaryAsync(CancellationToken ct = default)
    {
        var rows = await db.Set<PendingCommand>().AsNoTracking()
            .Select(c => new { c.Status, c.CreatedAtUtc })
            .ToListAsync(ct).ConfigureAwait(false);
        var unsettled = rows.Where(r => IsUnsettled(r.Status)).ToArray();
        var summary = new PendingCommandSummary(
            unsettled.Length,
            rows.Count(r => r.Status == SettlementState.PendingSettlement),
            rows.Count(r => r.Status == SettlementState.Rejected),
            rows.Count(r => r.Status == SettlementState.Conflict),
            rows.Count(r => r.Status == SettlementState.NeedsAttention),
            unsettled.Length == 0 ? null : unsettled.Min(r => r.CreatedAtUtc));
        return Result<PendingCommandSummary>.Ok(summary);
    }

    public async Task<Result<PagedResult<PendingCommandRecord>>> GetUnsettledPageAsync(int page, int pageSize,
        CancellationToken ct = default)
    {
        if (page < 1) return Result<PagedResult<PendingCommandRecord>>.Fail(ErrorCodes.ValidationFailed, "page < 1");
        var unsettled = UnsettledStates();
        var query = db.Set<PendingCommand>().AsNoTracking()
            .Where(c => unsettled.Contains(c.Status))
            .OrderByDescending(c => c.LocalSequence)
            .ThenBy(c => c.Id)
            .Select(c => c);
        var paged = await query.ToPagedAsync(page, pageSize, options.Value, ct).ConfigureAwait(false);
        var records = new PagedResult<PendingCommandRecord>(
            paged.Items.Select(Map).ToArray(), paged.TotalCount, paged.Page, paged.PageSize);
        return Result<PagedResult<PendingCommandRecord>>.Ok(records);
    }

    public async Task<Result<PagedResult<PendingCommandRecord>>> GetOldestUnsettledPageAsync(int page, int pageSize,
        CancellationToken ct = default)
    {
        if (page < 1) return Result<PagedResult<PendingCommandRecord>>.Fail(ErrorCodes.ValidationFailed, "page < 1");
        var unsettled = UnsettledStates();
        var query = db.Set<PendingCommand>().AsNoTracking()
            .Where(c => unsettled.Contains(c.Status))
            .OrderBy(c => c.LocalSequence)
            .ThenBy(c => c.Id)
            .Select(c => c);
        var paged = await query.ToPagedAsync(page, pageSize, options.Value, ct).ConfigureAwait(false);
        var records = new PagedResult<PendingCommandRecord>(
            paged.Items.Select(Map).ToArray(), paged.TotalCount, paged.Page, paged.PageSize);
        return Result<PagedResult<PendingCommandRecord>>.Ok(records);
    }

    private async Task<Result> SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result.Ok();
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException) { return Result.Fail(ErrorCodes.StoreUnavailable); }
        catch (SqliteException) { return Result.Fail(ErrorCodes.StoreUnavailable); }
    }

    private static SettlementState TargetState(SettlementOutcome outcome) => outcome switch
    {
        SettlementOutcome.Accepted => SettlementState.Settled,
        SettlementOutcome.Rejected => SettlementState.Rejected,
        SettlementOutcome.Conflict => SettlementState.Conflict,
        _ => SettlementState.PendingSettlement, // Deferred: stays pending, retried unchanged.
    };

    private static bool IsUnsettled(SettlementState state) =>
        state is not (SettlementState.Settled or SettlementState.Reversed);

    private static SettlementState[] UnsettledStates() =>
    [
        SettlementState.Draft, SettlementState.LocallyPosted, SettlementState.PendingSettlement,
        SettlementState.Rejected, SettlementState.Conflict, SettlementState.NeedsAttention,
    ];

    private static PendingCommandRecord Map(PendingCommand c) => new(
        c.Id, c.OperationId, c.TenantId, c.UserId, c.DeviceId, c.CommandType, c.AggregateType, c.AggregateId,
        c.PayloadJson, c.CommandFingerprint, c.LocalSequence, c.Status, c.CausationId, c.DependsOnOperationId,
        c.ExpectedAggregateVersion, c.CreatedAtUtc, c.LastAttemptUtc, c.Attempts, c.NextAttemptUtc, c.LastErrorCode,
        c.LastErrorDetail, c.FinancialEffect, c.ResponsibleRole, c.SettledAtUtc, c.ServerEntityId, c.ServerVersion,
        c.ClaimToken, c.ClaimedUntilUtc);
}
