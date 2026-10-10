using Animora.Domain.Results;
using Animora.Domain.Subscriptions;
using Xunit;

namespace Animora.Server.Tests.Subscriptions;

public sealed class SubscriptionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);
    private static readonly ActorRef Operator = new(ActorKind.Operator, "01ARZ3NDEKTSV4RRFFQ69G5FAV");
    private static Plan Plan(bool trial = true) => Assert.IsType<Plan>(
        Animora.Domain.Subscriptions.Plan.Create(trial ? "trial" : "standard", "Clinic", 3, trial ? 0 : 100, trial ? 14 : 0, Now).Value);
    private static Subscription Start(bool trial = true) => Assert.IsType<Subscription>(
        Subscription.Start("01ARZ3NDEKTSV4RRFFQ69G5FAT", Plan(trial), Now, Operator).Value);

    [Fact]
    public void Extend_Reduce_Renew_Cancel_Rules()
    {
        var s = Start();
        Assert.True(s.Extend(10, Now, Operator).IsSuccess);
        Assert.True(s.Reduce(Now.AddDays(18), Now, Operator).IsSuccess);
        Assert.True(s.Suspend("operator request", Now, Operator).IsSuccess);
        Assert.True(s.Reinstate(Now, Operator).IsSuccess);
        Assert.True(s.Renew(Now, Plan(false), Operator).IsSuccess);
        Assert.Single(s.BillingPeriods);
        Assert.True(s.Cancel(true, Now, Operator).IsSuccess);
        Assert.Equal(SubscriptionStatus.Cancelled, s.Status);
        Assert.True(s.Renew(Now, Plan(false), Operator).IsFailure);
        Assert.True(s.ChangeSeats(4, 3, Now).IsFailure);
    }

    [Fact]
    public void Reduce_NeverBeforeNow_NeverBelowPeriodStart()
    {
        var s = Start(); var stamp = s.ConcurrencyStamp;
        Assert.True(s.Reduce(Now.AddDays(-1), Now).IsFailure);
        Assert.True(s.Reduce(Now.AddDays(1), Now.AddDays(2)).IsFailure);
        Assert.Equal(stamp, s.ConcurrencyStamp);
        var paid = Start(false);
        Assert.True(paid.Reduce(paid.CurrentPeriodEndUtc.AddDays(-1), Now).IsFailure);
        Assert.True(paid.Extend(10, Now).IsSuccess);
        Assert.True(paid.Reduce(paid.BillingPeriods[0].EndUtc, Now).IsSuccess);
    }

    [Fact]
    public void Renew_BillingPeriodSequence_GapFree()
    {
        var s = Start(false); var plan = Plan(false);
        for (var i = 0; i < 5; i++) Assert.True(s.Renew(Now, plan).IsSuccess);
        Assert.Equal(Enumerable.Range(1, 6), s.BillingPeriods.Select(p => p.Sequence));
        Assert.Equal(7, s.NextPeriodSequence);
        for (var i = 1; i < s.BillingPeriods.Count; i++)
            Assert.Equal(s.BillingPeriods[i - 1].EndUtc, s.BillingPeriods[i].StartUtc);
    }

    [Fact]
    public void EventSequence_GapFree_AppendOnly()
    {
        var s = Start(); var first = s.Events[0]; var stamp = s.ConcurrencyStamp;
        Assert.True(s.Extend(1, Now, Operator).IsSuccess);
        Assert.NotEqual(stamp, s.ConcurrencyStamp);
        Assert.True(s.ChangeSeats(5, 3, Now, Operator).IsSuccess);
        Assert.True(s.ChangeSeats(1, 3, Now, Operator).IsFailure);
        Assert.Equal(Enumerable.Range(1, 3), s.Events.Select(e => e.Sequence));
        Assert.Equal(first, s.Events[0]);
        Assert.All(s.Events, e => Assert.Equal(Operator.ActorId, e.ActorId));
        Assert.Equal(4, s.NextEventSequence);
    }

    [Fact]
    public void ChangeSeats_BelowUsage_Rejected()
    {
        var s = Start();
        Assert.Equal(ErrorCodes.SeatLimitExceeded, s.ChangeSeats(2, 3, Now).Error);
        Assert.Equal(3, s.SeatLimit);
        Assert.True(s.ChangeSeats(3, 3, Now).IsSuccess);
        Assert.True(s.ChangeSeats(0, 0, Now).IsFailure);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Tick_TrialActiveToGraceToExpired_AndCancelAtPeriodEnd(bool trial)
    {
        var s = Start(trial); var end = s.CurrentPeriodEndUtc;
        Assert.True(s.Tick(end.AddTicks(-1)).IsSuccess);
        Assert.True(s.Tick(end).IsSuccess);
        Assert.Equal(SubscriptionStatus.GracePeriod, s.Status);
        Assert.Equal(end.AddDays(7), s.GraceEndsUtc);
        Assert.True(s.Tick(end.AddDays(7)).IsSuccess);
        Assert.Equal(SubscriptionStatus.Expired, s.Status);
        var count = s.Events.Count;
        Assert.True(s.Tick(end.AddDays(8)).IsSuccess);
        Assert.Equal(count, s.Events.Count);
        var cancelled = Start(trial);
        Assert.True(cancelled.Cancel(false, Now).IsSuccess);
        Assert.True(cancelled.Tick(cancelled.CurrentPeriodEndUtc).IsSuccess);
        Assert.Equal(SubscriptionStatus.Cancelled, cancelled.Status);
    }

    [Fact]
    public void Tick_LateSweep_ExpiresWithoutExtendingGrace()
    {
        var s = Start(); var end = s.CurrentPeriodEndUtc;
        Assert.True(s.Tick(end.AddDays(100)).IsSuccess);
        Assert.Equal(SubscriptionStatus.Expired, s.Status);
        Assert.Equal(end.AddDays(7), s.GraceEndsUtc);
    }

    [Fact]
    public void Plan_Validation_AndInactivePlan_FailClosed()
    {
        Assert.True(Animora.Domain.Subscriptions.Plan.Create("INVALID", "Name", 1, 0, 0, Now).IsFailure);
        Assert.True(Animora.Domain.Subscriptions.Plan.Create("ok", "Name", 0, 0, 0, Now).IsFailure);
        var plan = Plan(); plan.Deactivate();
        Assert.True(Subscription.Start("tenant", plan, Now).IsFailure);
        Assert.True(Start().ChangePlan(plan, Now).IsFailure);
    }
}
