namespace Animora.Contracts.Wire.Commands;

/// <summary>
/// The server's answer to a submitted <see cref="CommandEnvelope"/>. Carries the full sync-failure UX
/// contract (A-RULE 024): <b>what</b> failed (<see cref="ErrorCode"/>), <b>why</b> (<see cref="Detail"/>),
/// <b>does it have a financial effect</b> (<see cref="FinancialEffect"/>) and <b>who must act</b>
/// (<see cref="ResponsibleRole"/>). A bare failure is impossible by shape.
/// </summary>
/// <param name="OperationId">The command this settles.</param>
/// <param name="Outcome">Accept / Reject / Conflict / Defer.</param>
/// <param name="ErrorCode">Catalog error code when not accepted; <see langword="null"/> on acceptance.</param>
/// <param name="Detail">Developer-facing diagnostic string; never user-facing localized text.</param>
/// <param name="ServerEntityId">The server aggregate id, recorded on acceptance.</param>
/// <param name="ServerVersion">The opaque server version token, recorded on acceptance.</param>
/// <param name="SettledAtUtc">When the server produced this answer.</param>
/// <param name="FinancialEffect">Whether the command moved money.</param>
/// <param name="ResponsibleRole">The role that must act to resolve a non-settled command, when known.</param>
public sealed record SettlementResult(
    string OperationId,
    SettlementOutcome Outcome,
    string? ErrorCode,
    string? Detail,
    string? ServerEntityId,
    string? ServerVersion,
    DateTimeOffset SettledAtUtc,
    FinancialEffect FinancialEffect,
    string? ResponsibleRole);
