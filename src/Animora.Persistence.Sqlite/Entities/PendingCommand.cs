using Animora.Application.Commands;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Primitives;
using Animora.Domain.Results;

namespace Animora.Persistence.Sqlite.Entities;

/// <summary>
/// The durable, local life of one business intent (ARCHITECTURE.md §8.3/§12.1). Written in the same
/// transaction as the business change, so a kill at any instant loses nothing and duplicates nothing.
/// Never replicated.
/// </summary>
[LocalOnly]
public sealed class PendingCommand
{
    /// <summary>Fixed length of the SHA-256 hex fingerprint.</summary>
    public const int FingerprintLength = 64;

    private PendingCommand()
    {
    }

    public string Id { get; private set; } = string.Empty;
    public string OperationId { get; private set; } = string.Empty;
    public string TenantId { get; private set; } = string.Empty;
    public string UserId { get; private set; } = string.Empty;
    public string DeviceId { get; private set; } = string.Empty;
    public string CommandType { get; private set; } = string.Empty;
    public string AggregateType { get; private set; } = string.Empty;
    public string? AggregateId { get; private set; }
    public string PayloadJson { get; private set; } = string.Empty;
    public string CommandFingerprint { get; private set; } = string.Empty;
    public long LocalSequence { get; private set; }
    public SettlementState Status { get; private set; }
    public string? CausationId { get; private set; }
    public string? DependsOnOperationId { get; private set; }
    public long? ExpectedAggregateVersion { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? LastAttemptUtc { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset? NextAttemptUtc { get; private set; }
    public string? LastErrorCode { get; private set; }
    public string? LastErrorDetail { get; private set; }
    public FinancialEffect FinancialEffect { get; private set; }
    public string? ResponsibleRole { get; private set; }
    public DateTimeOffset? SettledAtUtc { get; private set; }
    public string? ServerEntityId { get; private set; }
    public string? ServerVersion { get; private set; }
    public DateTimeOffset? ClaimedUntilUtc { get; private set; }
    public string? ClaimToken { get; private set; }

    /// <summary>
    /// Records a new command in the caller's transaction. It starts <c>LocallyPosted</c>: the business change
    /// that rode this transaction already applied locally, and settlement is the server's to decide.
    /// </summary>
    internal static PendingCommand Create(CommandEnvelope envelope, DateTimeOffset nowUtc) => new()
    {
        Id = IdGenerator.NewId(nowUtc),
        OperationId = envelope.OperationId,
        TenantId = envelope.TenantId,
        UserId = envelope.UserId,
        DeviceId = envelope.DeviceId,
        CommandType = envelope.CommandType,
        AggregateType = envelope.AggregateType,
        AggregateId = envelope.AggregateId,
        PayloadJson = envelope.PayloadJson,
        CommandFingerprint = envelope.CommandFingerprint,
        LocalSequence = envelope.LocalSequence,
        Status = SettlementState.LocallyPosted,
        CausationId = envelope.CausationId,
        DependsOnOperationId = envelope.DependsOnOperationId,
        ExpectedAggregateVersion = envelope.ExpectedAggregateVersion,
        CreatedAtUtc = nowUtc,
        FinancialEffect = FinancialEffect.Unknown,
    };

    /// <summary>
    /// Applies a validated settlement. The caller has already checked the transition against
    /// <c>SettlementTransitions</c>; this only writes the outcome, effect, role and server references.
    /// </summary>
    internal void ApplySettlement(SettlementState status, SettlementResult result, DateTimeOffset nowUtc)
    {
        Status = status;
        LastErrorCode = result.ErrorCode;
        LastErrorDetail = result.Detail;
        FinancialEffect = result.FinancialEffect;
        ResponsibleRole = result.ResponsibleRole;
        SettledAtUtc = status is SettlementState.Settled or SettlementState.Rejected or SettlementState.Conflict
            or SettlementState.Reversed ? result.SettledAtUtc : SettledAtUtc;
        ServerEntityId = result.ServerEntityId ?? ServerEntityId;
        ServerVersion = result.ServerVersion ?? ServerVersion;
        ClearClaim();
    }

    /// <summary>
    /// Marks a release with backoff. Only a real failure (<paramref name="isFailure"/>) consumes the retry
    /// budget and can move the row to <c>NeedsAttention</c>; a deferral (offline, dependency unsettled) stays
    /// <c>PendingSettlement</c> and is retried unchanged, so an offline desktop never strands its own queue.
    /// The last error code is recorded either way, so the UI can explain a wait (A-RULE 024).
    /// </summary>
    internal void Release(ErrorCode error, TimeSpan backoff, DateTimeOffset nowUtc, bool isFailure)
    {
        if (isFailure) Attempts++;
        LastAttemptUtc = nowUtc;
        NextAttemptUtc = nowUtc + backoff;
        if (!error.IsNone) LastErrorCode = error.Code;
        Status = isFailure && RetryPolicy.IsExhausted(Attempts)
            ? SettlementState.NeedsAttention
            : SettlementState.PendingSettlement;
        ClearClaim();
    }

    private void ClearClaim()
    {
        ClaimToken = null;
        ClaimedUntilUtc = null;
    }
}
