namespace Animora.Persistence.PostgreSql.Commands;

/// <summary>
/// One row per <c>(TenantId, OperationId)</c> — the server-side idempotency record of a received command (S06).
/// <b>Not</b> a <c>SyncableEntity</c> (no <c>Version</c>/<c>Deleted</c>): it never replicates. Tenant scoping is applied
/// by the <c>"Tenant"</c> named query filter registered in <c>AnimoraServerDbContext</c>.
/// </summary>
public sealed class CommandIntakeRecord
{
    private CommandIntakeRecord()
    {
    }

    /// <summary>Owning tenant ULID (first column of the composite key).</summary>
    public string TenantId { get; private set; } = string.Empty;

    /// <summary>Client-generated business operation ULID (second column of the composite key).</summary>
    public string OperationId { get; private set; } = string.Empty;

    /// <summary>Catalog command type.</summary>
    public string CommandType { get; private set; } = string.Empty;

    /// <summary>SHA-256 hex fingerprint the server recomputed from <c>CommandType + payload</c>.</summary>
    public string Fingerprint { get; private set; } = string.Empty;

    /// <summary>Lifecycle status.</summary>
    public IntakeStatus Status { get; private set; }

    /// <summary>Settlement outcome (<c>SettlementOutcome</c> as a number); <see langword="null"/> until settled.</summary>
    public short? Outcome { get; private set; }

    /// <summary>Stable error code of a non-accepted outcome.</summary>
    public string? ErrorCode { get; private set; }

    /// <summary>Short human detail (≤ 512 chars); never a secret.</summary>
    public string? Detail { get; private set; }

    /// <summary>Primary server entity id the command created/affected (slot 0 of the id map).</summary>
    public string? ServerEntityId { get; private set; }

    /// <summary>Applied canonical aggregate version (uuid) on acceptance.</summary>
    public Guid? ServerVersion { get; private set; }

    /// <summary><c>FinancialEffect</c> as a number.</summary>
    public short FinancialEffect { get; private set; }

    /// <summary>Role responsible for resolving a rejection.</summary>
    public string? ResponsibleRole { get; private set; }

    /// <summary>When intake first received the command, UTC.</summary>
    public DateTimeOffset ReceivedUtc { get; private set; }

    /// <summary>When the command settled, UTC.</summary>
    public DateTimeOffset? SettledUtc { get; private set; }

    /// <summary>Fencing token of the current execution attempt; only its holder may complete the row.</summary>
    public string? AttemptId { get; private set; }

    /// <summary>Opaque identifier of the instance holding the lease.</summary>
    public string? LeaseOwner { get; private set; }

    /// <summary>The lease expiry, UTC; an <see cref="IntakeStatus.InProgress"/> row past it is recoverable.</summary>
    public DateTimeOffset? LeaseUntilUtc { get; private set; }

    /// <summary>Number of leases taken on this row (1 for the first execution, +1 per recovery/re-run).</summary>
    public short AttemptCount { get; private set; }

    /// <summary>Canonical payload JSON; nulled by compaction once the full-record window passed.</summary>
    public string? PayloadJson { get; private set; }

    /// <summary>The operation this command depends on, when any.</summary>
    public string? DependsOnOperationId { get; private set; }

    /// <summary>Correlation id tying the row to request logs.</summary>
    public string CorrelationId { get; private set; } = string.Empty;

    /// <summary>Creates the first-attempt row (<see cref="IntakeStatus.InProgress"/>, leased) for <c>TryBeginAsync</c>.</summary>
    public static CommandIntakeRecord Begin(
        string tenantId,
        string operationId,
        string commandType,
        string fingerprint,
        string payloadJson,
        string? dependsOnOperationId,
        string correlationId,
        string attemptId,
        string leaseOwner,
        DateTimeOffset receivedUtc,
        DateTimeOffset leaseUntilUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandType);
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        ArgumentNullException.ThrowIfNull(payloadJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(attemptId);
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseOwner);

        return new CommandIntakeRecord
        {
            TenantId = tenantId,
            OperationId = operationId,
            CommandType = commandType,
            Fingerprint = fingerprint,
            Status = IntakeStatus.InProgress,
            FinancialEffect = 0,
            ReceivedUtc = receivedUtc.ToUniversalTime(),
            AttemptId = attemptId,
            LeaseOwner = leaseOwner,
            LeaseUntilUtc = leaseUntilUtc.ToUniversalTime(),
            AttemptCount = 1,
            PayloadJson = payloadJson,
            DependsOnOperationId = dependsOnOperationId,
            CorrelationId = correlationId,
        };
    }
}
