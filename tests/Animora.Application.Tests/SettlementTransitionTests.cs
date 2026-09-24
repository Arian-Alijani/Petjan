using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Xunit;

namespace Animora.Application.Tests;

/// <summary>
/// B02 acceptance: all 64 settlement-state pairs are evaluated against the allow-table; only the tabled ones
/// succeed and every other pair is <c>SETTLEMENT_INVALID_TRANSITION</c>.
/// </summary>
public sealed class SettlementTransitionTests
{
    // The exact allow-table from PHASE-B02 (client and server both consult this).
    private static readonly HashSet<(SettlementState From, SettlementState To)> Allowed =
    [
        (SettlementState.Draft, SettlementState.LocallyPosted),
        (SettlementState.Draft, SettlementState.Rejected),
        (SettlementState.LocallyPosted, SettlementState.PendingSettlement),
        (SettlementState.LocallyPosted, SettlementState.Rejected),
        (SettlementState.LocallyPosted, SettlementState.Reversed),
        (SettlementState.PendingSettlement, SettlementState.Settled),
        (SettlementState.PendingSettlement, SettlementState.Rejected),
        (SettlementState.PendingSettlement, SettlementState.Conflict),
        (SettlementState.PendingSettlement, SettlementState.NeedsAttention),
        (SettlementState.PendingSettlement, SettlementState.PendingSettlement),
        (SettlementState.Conflict, SettlementState.NeedsAttention),
        (SettlementState.Conflict, SettlementState.Reversed),
        (SettlementState.Conflict, SettlementState.Settled),
        (SettlementState.Rejected, SettlementState.NeedsAttention),
        (SettlementState.Rejected, SettlementState.Reversed),
        (SettlementState.NeedsAttention, SettlementState.Reversed),
        (SettlementState.NeedsAttention, SettlementState.Settled),
        (SettlementState.NeedsAttention, SettlementState.Rejected),
        (SettlementState.Settled, SettlementState.Reversed),
    ];

    [Fact]
    public void EveryStatePair_MatchesTheAllowTable()
    {
        var states = Enum.GetValues<SettlementState>();
        Assert.Equal(64, states.Length * states.Length);

        foreach (var from in states)
        foreach (var to in states)
        {
            var expected = Allowed.Contains((from, to));
            var result = SettlementTransitions.EnsureAllowed(from, to);
            Assert.Equal(expected, result.IsSuccess);
            Assert.Equal(expected, SettlementTransitions.IsAllowed(from, to));
            if (!expected)
                Assert.Equal(ErrorCodes.SettlementInvalidTransition, result.Error);
        }
    }

    [Fact]
    public void Reversed_IsTerminal()
    {
        foreach (var to in Enum.GetValues<SettlementState>())
            Assert.False(SettlementTransitions.IsAllowed(SettlementState.Reversed, to));
    }

    [Fact]
    public void InvalidTransition_DetailNamesThePair()
    {
        var result = SettlementTransitions.EnsureAllowed(SettlementState.Draft, SettlementState.Settled);
        Assert.True(result.IsFailure);
        Assert.Contains("Draft", result.Detail, StringComparison.Ordinal);
        Assert.Contains("Settled", result.Detail, StringComparison.Ordinal);
    }
}
