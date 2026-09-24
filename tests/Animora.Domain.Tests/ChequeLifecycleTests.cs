using System;
using System.Collections.Generic;
using Animora.Domain.Finance;
using Animora.Domain.Results;
using Animora.Domain.ValueObjects;
using Xunit;

namespace Animora.Domain.Tests;

/// <summary>
/// The cheque lifecycle: only the tabled transitions succeed, every accepted transition appends
/// exactly one history entry, and a cheque always names its counterparty.
/// </summary>
public sealed class ChequeLifecycleTests
{
    private static readonly DateTimeOffset Issued = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private static readonly HashSet<(ChequeState From, ChequeState To)> Allowed =
    [
        (ChequeState.Registered, ChequeState.InPortfolio),
        (ChequeState.Registered, ChequeState.Cancelled),
        (ChequeState.InPortfolio, ChequeState.Deposited),
        (ChequeState.InPortfolio, ChequeState.Returned),
        (ChequeState.InPortfolio, ChequeState.Cancelled),
        (ChequeState.Deposited, ChequeState.Cleared),
        (ChequeState.Deposited, ChequeState.Bounced),
        (ChequeState.Bounced, ChequeState.InPortfolio),
        (ChequeState.Bounced, ChequeState.Returned),
        (ChequeState.Bounced, ChequeState.Settled),
    ];

    [Fact]
    public void AllStatePairs_MatchTheTable()
    {
        var states = Enum.GetValues<ChequeState>();
        Assert.Equal(64, states.Length * states.Length);

        foreach (var from in states)
        {
            foreach (var to in states)
            {
                Assert.Equal(Allowed.Contains((from, to)), Cheque.IsTransitionAllowed(from, to));
            }
        }
    }

    private static Cheque RegisteredInbound(FakeClock clock, string ownerId = "OWN1") =>
        Cheque.Register("T1", "CHQ-1", ChequeDirection.Inbound, Money.Irt(1_000_000),
            Issued, clock.UtcNow, ownerId: ownerId).Value!;

    [Fact]
    public void InboundCheque_RequiresAnOwner()
    {
        var result = Cheque.Register("T1", "CHQ-1", ChequeDirection.Inbound, Money.Irt(1_000),
            Issued, Issued, ownerId: "   ");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.ChequeCounterpartyRequired, result.Error);
    }

    [Fact]
    public void OutboundCheque_RequiresASupplier()
    {
        var result = Cheque.Register("T1", "CHQ-1", ChequeDirection.Outbound, Money.Irt(1_000),
            Issued, Issued, supplierId: null);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.ChequeCounterpartyRequired, result.Error);

        Assert.True(Cheque.Register("T1", "CHQ-2", ChequeDirection.Outbound, Money.Irt(1_000),
            Issued, Issued, supplierId: "SUP1").IsSuccess);
    }

    [Fact]
    public void NonPositiveAmount_IsRefused()
    {
        var result = Cheque.Register("T1", "CHQ-1", ChequeDirection.Inbound, Money.ZeroIrt,
            Issued, Issued, ownerId: "OWN1");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.ValidationFailed, result.Error);
    }

    [Fact]
    public void EachAcceptedTransition_AppendsExactlyOneHistoryEntry()
    {
        var clock = new FakeClock(Issued);
        var cheque = RegisteredInbound(clock);

        Assert.True(cheque.MoveToPortfolio("U1", clock).IsSuccess);
        Assert.Single(cheque.History);
        Assert.Equal(ChequeState.Registered, cheque.History[0].FromState);
        Assert.Equal(ChequeState.InPortfolio, cheque.History[0].ToState);

        Assert.True(cheque.Deposit("U1", clock).IsSuccess);
        Assert.Equal(2, cheque.History.Count);

        Assert.True(cheque.MarkCleared("U1", clock).IsSuccess);
        Assert.Equal(3, cheque.History.Count);
        Assert.Equal(ChequeState.Cleared, cheque.State);
        Assert.True(cheque.IsTerminal);
    }

    [Fact]
    public void RejectedTransition_ChangesNothing()
    {
        var clock = new FakeClock(Issued);
        var cheque = RegisteredInbound(clock);

        var result = cheque.Deposit("U1", clock); // Registered -> Deposited is not tabled.

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.ChequeInvalidTransition, result.Error);
        Assert.Empty(cheque.History);
        Assert.Equal(ChequeState.Registered, cheque.State);
    }

    [Fact]
    public void BouncedCheque_CanReturnToPortfolio()
    {
        var clock = new FakeClock(Issued);
        var cheque = RegisteredInbound(clock);
        cheque.MoveToPortfolio("U1", clock);
        cheque.Deposit("U1", clock);

        Assert.True(cheque.MarkBounced("U1", "insufficient funds", clock).IsSuccess);
        Assert.Equal(ChequeState.Bounced, cheque.State);
        Assert.Equal("insufficient funds", cheque.History[^1].Reason);

        Assert.True(cheque.MoveToPortfolio("U1", clock).IsSuccess);
        Assert.Equal(ChequeState.InPortfolio, cheque.State);
    }

    [Fact]
    public void MarkBounced_RequiresAReason()
    {
        var clock = new FakeClock(Issued);
        var cheque = RegisteredInbound(clock);
        cheque.MoveToPortfolio("U1", clock);
        cheque.Deposit("U1", clock);

        Assert.Throws<ArgumentException>(() => cheque.MarkBounced("U1", "  ", clock));
    }
}
