using Animora.Contracts.Wire.Commands;
using Animora.Domain.Primitives;

namespace Animora.Application.Commands;

/// <summary>
/// The read-side projection of a durable <c>PendingCommand</c> row. Boundaries expose this record, never the
/// EF entity (CONVENTIONS §2/§5): the entity lives in the persistence assembly, which the Application layer
/// and the Desktop never reference.
/// </summary>
public sealed record PendingCommandRecord(
    string Id,
    string OperationId,
    string TenantId,
    string UserId,
    string DeviceId,
    string CommandType,
    string AggregateType,
    string? AggregateId,
    string PayloadJson,
    string CommandFingerprint,
    long LocalSequence,
    SettlementState Status,
    string? CausationId,
    string? DependsOnOperationId,
    long? ExpectedAggregateVersion,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastAttemptUtc,
    int Attempts,
    DateTimeOffset? NextAttemptUtc,
    string? LastErrorCode,
    string? LastErrorDetail,
    FinancialEffect FinancialEffect,
    string? ResponsibleRole,
    DateTimeOffset? SettledAtUtc,
    string? ServerEntityId,
    string? ServerVersion,
    string? ClaimToken,
    DateTimeOffset? ClaimedUntilUtc);
