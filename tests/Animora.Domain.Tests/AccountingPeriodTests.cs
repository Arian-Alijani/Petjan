using System;
using System.Collections.Generic;
using Animora.Domain.Finance;
using Animora.Domain.Results;
using Xunit;

namespace Animora.Domain.Tests;

/// <summary>
/// The accounting period gate: each of the three close blockers refuses with
/// <c>PERIOD_CLOSE_BLOCKED</c>, <see cref="AccountingPeriod.EnsurePostable"/> rejects closed and
/// locked periods, and the machine is strictly forward — no path back to Open.
/// </summary>
public sealed class AccountingPeriodTests
{
    private static readonly DateTimeOffset Start = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly HashSet<(PeriodState From, PeriodState To)> Allowed =
    [
        (PeriodState.Open, PeriodState.Closing),
        (PeriodState.Closing, PeriodState.Closed),
        (PeriodState.Closed, PeriodState.Locked),
    ];

    private static AccountingPeriod Open() =>
        AccountingPeriod.Create("T1", "1405-07", Start, End).Value!;

    [Fact]
    public void AllStatePairs_MatchTheTable_AndNeverReopen()
    {
        var states = Enum.GetValues<PeriodState>();
        foreach (var from in states)
        {
            foreach (var to in states)
            {
                Assert.Equal(Allowed.Contains((from, to)), AccountingPeriod.IsTransitionAllowed(from, to));
                if (to == PeriodState.Open)
                {
                    Assert.False(AccountingPeriod.IsTransitionAllowed(from, PeriodState.Open));
                }
            }
        }
    }

    [Fact]
    public void Create_WithEndNotAfterStart_IsRefused()
    {
        var result = AccountingPeriod.Create("T1", "1405-07", Start, Start);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.ValidationFailed, result.Error);
    }

    [Theory]
    [InlineData(true, false, false, "UNSETTLED_COMMANDS")]
    [InlineData(false, true, false, "OPEN_CASH_SESSIONS")]
    [InlineData(false, false, true, "DRAFT_INVOICES")]
    public void EachCloseBlocker_RefusesWithItsName(bool unsettled, bool openCash, bool drafts, string blocker)
    {
        var clock = new FakeClock(Start);
        var period = Open();
        period.BeginClose();

        var result = period.Close(unsettled, openCash, drafts, clock, "JE-CLOSE", "U1");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.PeriodCloseBlocked, result.Error);
        Assert.Contains(blocker, result.Detail);
        Assert.Equal(PeriodState.Closing, period.State);
    }

    [Fact]
    public void Close_WithNoBlockers_Succeeds()
    {
        var clock = new FakeClock(Start);
        var period = Open();
        period.BeginClose();

        var result = period.Close(false, false, false, clock, "JE-CLOSE", "U1", "month end");

        Assert.True(result.IsSuccess);
        Assert.Equal(PeriodState.Closed, period.State);
        Assert.Equal("JE-CLOSE", period.ClosingEntryId);
        Assert.Equal(clock.UtcNow, period.ClosedUtc);
    }

    [Fact]
    public void EnsurePostable_AcceptsOpenAndClosing_RejectsClosedAndLocked()
    {
        var clock = new FakeClock(Start);

        var open = Open();
        Assert.True(AccountingPeriod.EnsurePostable(open).IsSuccess);

        var closing = Open();
        closing.BeginClose();
        Assert.True(AccountingPeriod.EnsurePostable(closing).IsSuccess);

        var closed = Open();
        closed.BeginClose();
        closed.Close(false, false, false, clock, "JE-1", "U1");
        var closedResult = AccountingPeriod.EnsurePostable(closed);
        Assert.True(closedResult.IsFailure);
        Assert.Equal(ErrorCodes.PeriodClosed, closedResult.Error);

        var locked = Open();
        locked.BeginClose();
        locked.Close(false, false, false, clock, "JE-2", "U1");
        Assert.True(locked.Lock().IsSuccess);
        Assert.True(AccountingPeriod.EnsurePostable(locked).IsFailure);
        Assert.Equal(PeriodState.Locked, locked.State);
    }

    [Fact]
    public void ClosedPeriod_RefusesAnyTransitionBack()
    {
        var clock = new FakeClock(Start);
        var period = Open();
        period.BeginClose();
        period.Close(false, false, false, clock, "JE-1", "U1");

        var reopen = period.BeginClose();
        Assert.True(reopen.IsFailure);
        Assert.Equal(ErrorCodes.PeriodInvalidTransition, reopen.Error);
    }
}
