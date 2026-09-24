namespace Animora.Domain.Primitives;

/// <summary>
/// Where a business command stands in its settlement life cycle (ARCHITECTURE.md §8.3). The Domain owns this
/// vocabulary; the local <c>PendingCommand</c> table stores it as an <c>int</c> (CONVENTIONS §5) and the
/// Desktop always surfaces it — a settlement state is never hidden from the user.
/// </summary>
public enum SettlementState
{
    /// <summary>Recorded but not yet posted locally.</summary>
    Draft,

    /// <summary>Applied to local provisional state; not yet sent to the server.</summary>
    LocallyPosted,

    /// <summary>Submitted (or queued for submission); awaiting the server's decision.</summary>
    PendingSettlement,

    /// <summary>The server accepted and applied the command — the terminal success state.</summary>
    Settled,

    /// <summary>The server refused the command; it will not apply as-is.</summary>
    Rejected,

    /// <summary>The command conflicted with server state; domain policy decides the resolution.</summary>
    Conflict,

    /// <summary>A compensating entry was applied; the original effect is offset.</summary>
    Reversed,

    /// <summary>Retries are exhausted or a human must intervene; never silently dropped.</summary>
    NeedsAttention,
}
