using Animora.Domain.Results;

namespace Animora.Persistence.PostgreSql.Commands;

/// <summary>Outcome of <see cref="ICommandIntakeStore.TryBeginAsync"/>.</summary>
/// <param name="Inserted"><see langword="true"/> when this call created the row (the caller owns the first attempt).</param>
/// <param name="Existing">The row that already existed when <paramref name="Inserted"/> is <see langword="false"/>.</param>
public sealed record IntakeBeginResult(bool Inserted, CommandIntakeRecord? Existing);

/// <summary>One server-allocated aggregate id a command created.</summary>
/// <param name="Slot">Zero-based slot; slot 0 is the primary entity.</param>
/// <param name="EntityType">Aggregate/entity type name (≤ 32 chars).</param>
/// <param name="ServerEntityId">The server-allocated ULID.</param>
public sealed record CommandIdMapItem(short Slot, string EntityType, string ServerEntityId);

/// <summary>The terminal outcome a fenced attempt writes (same transaction as the effect).</summary>
/// <param name="OperationId">Operation being settled.</param>
/// <param name="AttemptId">Fencing token of the attempt that holds the lease.</param>
/// <param name="Outcome"><c>SettlementOutcome</c> as a number.</param>
/// <param name="ErrorCode">Stable code of a non-accepted outcome.</param>
/// <param name="Detail">Short detail, truncated to 512 chars.</param>
/// <param name="ServerEntityId">Primary created/affected id.</param>
/// <param name="ServerVersion">Applied aggregate version (uuid).</param>
/// <param name="FinancialEffect"><c>FinancialEffect</c> as a number.</param>
/// <param name="ResponsibleRole">Role that resolves a rejection.</param>
/// <param name="SettledUtc">Settlement instant, UTC.</param>
/// <param name="Ids">Every created id (slot 0 = primary).</param>
public sealed record IntakeSettlement(
    string OperationId,
    string AttemptId,
    short Outcome,
    string? ErrorCode,
    string? Detail,
    string? ServerEntityId,
    Guid? ServerVersion,
    short FinancialEffect,
    string? ResponsibleRole,
    DateTimeOffset SettledUtc,
    IReadOnlyList<CommandIdMapItem> Ids);

/// <summary>
/// The tenant-scoped idempotency store behind command intake (S06). Every method operates on the tenant bound in
/// the ambient tenant context and fails closed (<c>TenantContextMissingException</c>) when none is bound.
/// </summary>
public interface ICommandIntakeStore
{
    /// <summary>
    /// Single <c>INSERT … ON CONFLICT (TenantId, OperationId) DO NOTHING</c>. When the row already existed the
    /// stored row is returned (never modified), so concurrent retries of one operation produce exactly one winner.
    /// </summary>
    Task<IntakeBeginResult> TryBeginAsync(CommandIntakeRecord record, CancellationToken ct = default);

    /// <summary>Reads the row for <paramref name="operationId"/> (no tracking); <see langword="null"/> when absent.</summary>
    Task<CommandIntakeRecord?> FindAsync(string operationId, CancellationToken ct = default);

    /// <summary>
    /// Fenced settle: writes the terminal outcome and the <c>CommandIdMap</c> rows <b>only</b> while
    /// <paramref name="settlement"/>'s attempt still holds the row (<c>Status = InProgress AND AttemptId = …</c>).
    /// Call inside the unit-of-work transaction that carries the effect, so <c>Settled ⇔ effect committed</c>.
    /// Returns <c>ConcurrencyConflict</c> when the attempt was fenced out.
    /// </summary>
    Task<Result> CompleteAsync(IntakeSettlement settlement, CancellationToken ct = default);

    /// <summary>Extends the lease of the current attempt (compare-and-set on <c>AttemptId</c>). <see langword="false"/> when fenced out.</summary>
    Task<bool> RenewLeaseAsync(string operationId, string attemptId, DateTimeOffset leaseUntilUtc, CancellationToken ct = default);

    /// <summary>
    /// Takes over an <c>InProgress</c> row whose lease expired, or a <c>Failed</c> row, with a NEW fencing token
    /// (compare-and-set on the observed <paramref name="observedAttemptId"/>). Increments the attempt counter.
    /// <see langword="false"/> when another instance won the race or the row is no longer recoverable.
    /// </summary>
    Task<bool> TryAcquireAttemptAsync(
        string operationId,
        string? observedAttemptId,
        string newAttemptId,
        string leaseOwner,
        DateTimeOffset nowUtc,
        DateTimeOffset leaseUntilUtc,
        CancellationToken ct = default);

    /// <summary>Marks an expired <c>InProgress</c> row <c>Failed</c> (fenced on the observed attempt). <see langword="false"/> when lost the race.</summary>
    Task<bool> MarkFailedAsync(string operationId, string observedAttemptId, string? detail, CancellationToken ct = default);

    /// <summary>
    /// Lists <c>InProgress</c> rows of the bound tenant whose lease expired before <paramref name="nowUtc"/>
    /// (oldest first). <paramref name="limit"/> is clamped to 1–<see cref="MaxRecoveryBatch"/>.
    /// </summary>
    Task<IReadOnlyList<CommandIntakeRecord>> ListExpiredLeasesAsync(DateTimeOffset nowUtc, int limit, CancellationToken ct = default);

    /// <summary>Every id the command created, ordered by slot.</summary>
    Task<IReadOnlyList<CommandIdMapItem>> GetIdMapAsync(string operationId, CancellationToken ct = default);

    /// <summary>Upper bound of <see cref="ListExpiredLeasesAsync"/> (and the recovery worker batch).</summary>
    public const int MaxRecoveryBatch = 50;
}
