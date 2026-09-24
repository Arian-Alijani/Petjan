using Animora.Domain.Results;

namespace Animora.Domain.Primitives;

/// <summary>
/// The <b>only</b> authority on legal <see cref="SettlementState"/> changes (ARCHITECTURE.md §8.3). Both the
/// client and the server consult this table; no other code may encode a settlement transition. The table is a
/// static allow-list so an illegal move is a returned <c>SETTLEMENT_INVALID_TRANSITION</c>, never a silent
/// state write.
/// </summary>
public static class SettlementTransitions
{
    /// <summary>
    /// Returns <c>Ok</c> when <paramref name="from"/> → <paramref name="to"/> is tabled, otherwise
    /// <c>SETTLEMENT_INVALID_TRANSITION</c> with the pair named in <c>Detail</c>.
    /// </summary>
    public static Result EnsureAllowed(SettlementState from, SettlementState to)
    {
        // Same-state re-entry is allowed only for the retry loop (PendingSettlement → PendingSettlement);
        // every other state is a one-way move, so an identical no-op pair is rejected.
        if (IsAllowed(from, to)) return Result.Ok();
        return Result.Fail(ErrorCodes.SettlementInvalidTransition, $"{from} -> {to}");
    }

    /// <summary>Pure predicate over the allow-table (exposed for tests and the server's own validation).</summary>
    public static bool IsAllowed(SettlementState from, SettlementState to) => from switch
    {
        SettlementState.Draft => to is SettlementState.LocallyPosted or SettlementState.Rejected,
        SettlementState.LocallyPosted => to is SettlementState.PendingSettlement or SettlementState.Rejected
            or SettlementState.Reversed,
        SettlementState.PendingSettlement => to is SettlementState.Settled or SettlementState.Rejected
            or SettlementState.Conflict or SettlementState.NeedsAttention or SettlementState.PendingSettlement,
        SettlementState.Conflict => to is SettlementState.NeedsAttention or SettlementState.Reversed
            or SettlementState.Settled,
        SettlementState.Rejected => to is SettlementState.NeedsAttention or SettlementState.Reversed,
        SettlementState.NeedsAttention => to is SettlementState.Reversed or SettlementState.Settled
            or SettlementState.Rejected,
        SettlementState.Settled => to is SettlementState.Reversed,
        // Reversed is terminal.
        SettlementState.Reversed => false,
        _ => false,
    };
}
