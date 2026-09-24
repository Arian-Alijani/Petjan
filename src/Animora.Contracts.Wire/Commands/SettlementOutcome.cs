namespace Animora.Contracts.Wire.Commands;

/// <summary>
/// How the server settled a submitted command (ARCHITECTURE.md §8.3). <see cref="Deferred"/> is not a
/// failure: the command is still pending (offline / dependency unsettled) and must be retried unchanged.
/// </summary>
public enum SettlementOutcome
{
    /// <summary>The command was applied; the aggregate is now settled on the server.</summary>
    Accepted,

    /// <summary>The command was refused by a business rule and will not apply as-is.</summary>
    Rejected,

    /// <summary>The command conflicted with server state; domain policy decides the resolution (A-RULE 004).</summary>
    Conflict,

    /// <summary>No decision yet — the command stays pending and is retried unchanged (never a new OperationId).</summary>
    Deferred,
}
