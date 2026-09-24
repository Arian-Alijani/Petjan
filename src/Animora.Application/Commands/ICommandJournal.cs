using Animora.Application.Abstractions;
using Animora.Application.UseCases.Sync;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Results;

namespace Animora.Application.Commands;

/// <summary>
/// The durable, idempotent command journal (ARCHITECTURE.md §8.3, §12.1). Every business intent gets a
/// crash-safe local life here: an enqueue that rides the caller's transaction, an atomic claim, a validated
/// settlement, and the unsettled-aggregate guard E05 uses to protect unsent intent.
/// </summary>
public interface ICommandJournal
{
    /// <summary>
    /// Records the command in the current transaction — <b>never</b> calls <c>SaveChanges</c> (ARCHITECTURE.md
    /// §12.1). Re-enqueueing an existing <c>OperationId</c> with an identical fingerprint returns the stored
    /// record (replay); a different fingerprint fails with <c>IDEMPOTENCY_FINGERPRINT_MISMATCH</c> and raises an
    /// integrity alert (A-RULE 019).
    /// </summary>
    Task<Result<PendingCommandRecord>> EnqueueAsync(CommandEnvelope envelope, CancellationToken ct = default);

    /// <summary>Finds a command by its <c>OperationId</c>; <c>null</c> when this installation never saw it.</summary>
    Task<Result<PendingCommandRecord?>> FindByOperationIdAsync(string operationId, CancellationToken ct = default);

    /// <summary>Validates and stores a server settlement: outcome, financial effect and responsible role.</summary>
    Task<Result> ApplySettlementAsync(SettlementResult result, CancellationToken ct = default);

    /// <summary>
    /// The atomic claim (ARCHITECTURE.md §12.1): one conditional <c>UPDATE</c> takes up to <paramref name="max"/>
    /// eligible rows under a lease and returns exactly the rows it won. An expired lease is reclaimable, so a
    /// crashed processor never strands work (A-RULE 026).
    /// </summary>
    Task<Result<IReadOnlyList<PendingCommandRecord>>> ClaimBatchAsync(int max, TimeSpan lease,
        CancellationToken ct = default);

    /// <summary>
    /// Releases a claim with a backoff and schedules the next attempt. A deferral (<see cref="ErrorCode.None"/>
    /// for offline, <c>DEPENDENCY_UNSETTLED</c> for a waiting prerequisite) does <b>not</b> consume the retry
    /// budget; a real failure does, and after the budget is spent the row moves to <c>NeedsAttention</c> with
    /// its last error preserved.
    /// </summary>
    Task<Result> ReleaseAsync(string operationId, ErrorCode error, TimeSpan backoff, CancellationToken ct = default);

    /// <summary>The unsettled commands for one aggregate — the guard against an inbound pull overwriting intent (A-RULE 017).</summary>
    Task<Result<IReadOnlyList<PendingCommandRecord>>> GetUnsettledForAggregateAsync(string aggregateType,
        string aggregateId, CancellationToken ct = default);

    /// <summary>Counts by settlement state plus the oldest unsettled creation instant — the UI sync badge seam.</summary>
    Task<Result<PendingCommandSummary>> GetSummaryAsync(CancellationToken ct = default);

    /// <summary>A bounded, stable page of unsettled commands, newest first — the UI sync list seam.</summary>
    Task<Result<PagedResult<PendingCommandRecord>>> GetUnsettledPageAsync(int page, int pageSize,
        CancellationToken ct = default);
}
