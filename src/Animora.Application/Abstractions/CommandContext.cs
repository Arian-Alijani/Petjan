namespace Animora.Application.Abstractions;

/// <summary>
/// The only carrier of ambient identity into a command handler — no handler reads a static or
/// current-user singleton. Always carries an <see cref="OperationId"/> (A-RULE 005).
/// </summary>
public sealed record CommandContext(
    string TenantId,
    string UserId,
    string DeviceId,
    string OperationId,
    string CorrelationId,
    string? CausationId,
    string? DependsOnOperationId,
    long? ExpectedAggregateVersion,
    DateTimeOffset CreatedAtUtc);
