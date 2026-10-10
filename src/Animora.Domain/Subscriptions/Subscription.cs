using System.Text.Json;
using Animora.Domain.Primitives;
using Animora.Domain.Results;

namespace Animora.Domain.Subscriptions;

/// <summary>Vendor-global subscription. Counters and appended records commit with the row's concurrency stamp.</summary>
public sealed class Subscription
{
    private readonly List<BillingPeriod> _billingPeriods = [];
    private readonly List<SubscriptionEvent> _events = [];
    private Subscription() { }
    public string Id { get; private set; } = string.Empty;
    public string TenantId { get; private set; } = string.Empty;
    public string PlanId { get; private set; } = string.Empty;
    public SubscriptionStatus Status { get; private set; }
    public int SeatLimit { get; private set; }
    public DateTimeOffset CurrentPeriodStartUtc { get; private set; }
    public DateTimeOffset CurrentPeriodEndUtc { get; private set; }
    public DateTimeOffset? GraceEndsUtc { get; private set; }
    public DateTimeOffset? CancelRequestedUtc { get; private set; }
    public bool CancelAtPeriodEnd { get; private set; }
    public int NextPeriodSequence { get; private set; } = 1;
    public int NextEventSequence { get; private set; } = 1;
    // UUID is an optimistic-concurrency token, not a business entity identifier.
    public string ConcurrencyStamp { get; private set; } = Guid.NewGuid().ToString();
    public DateTimeOffset CreatedUtc { get; private set; }
    public DateTimeOffset UpdatedUtc { get; private set; }
    public IReadOnlyList<BillingPeriod> BillingPeriods => _billingPeriods.AsReadOnly();
    public IReadOnlyList<SubscriptionEvent> Events => _events.AsReadOnly();

    public static Result<Subscription> Start(string tenantId, Plan plan, DateTimeOffset now,
        ActorRef? actor = null, bool startTrial = true, int? seatLimitOverride = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (string.IsNullOrWhiteSpace(tenantId) || tenantId.Length > 26 || !plan.IsActive || seatLimitOverride is < 1)
            return Result<Subscription>.Fail(ErrorCodes.ValidationFailed, "invalid_subscription");
        now = now.ToUniversalTime();
        var trial = startTrial && plan.TrialDays > 0;
        var subscription = new Subscription
        {
            Id = IdGenerator.NewId(now), TenantId = tenantId, PlanId = plan.Id,
            Status = trial ? SubscriptionStatus.Trial : SubscriptionStatus.Active,
            SeatLimit = seatLimitOverride ?? plan.DefaultSeatLimit,
            CurrentPeriodStartUtc = now, CurrentPeriodEndUtc = trial ? now.AddDays(plan.TrialDays) : now.AddMonths(1),
            CreatedUtc = now, UpdatedUtc = now,
        };
        if (!trial) subscription.AppendPeriod(plan);
        subscription.AppendEvent("subscription.created", now, actor, new { plan.Code, subscription.SeatLimit, trial });
        return Result<Subscription>.Ok(subscription);
    }

    public Result Extend(int days, DateTimeOffset now, ActorRef? actor = null)
    {
        if (days is < 1 or > 3650) return Result.Fail(ErrorCodes.ValidationFailed, "invalid_extension_days");
        return Extend(CurrentPeriodEndUtc.AddDays(days), now, actor);
    }

    public Result Extend(DateTimeOffset untilUtc, DateTimeOffset now, ActorRef? actor = null)
    {
        if (Status is not (SubscriptionStatus.Active or SubscriptionStatus.Trial or SubscriptionStatus.GracePeriod))
            return Result.Fail(ErrorCodes.SubscriptionInvalidTransition);
        if (untilUtc <= CurrentPeriodEndUtc || untilUtc <= now)
            return Result.Fail(ErrorCodes.ValidationFailed, "extension_must_move_forward");
        CurrentPeriodEndUtc = untilUtc.ToUniversalTime();
        if (Status == SubscriptionStatus.GracePeriod) Status = SubscriptionStatus.Active;
        GraceEndsUtc = null;
        AppendEvent("subscription.extended", now, actor, new { untilUtc = CurrentPeriodEndUtc });
        return Result.Ok();
    }

    public Result Reduce(DateTimeOffset untilUtc, DateTimeOffset now, ActorRef? actor = null)
    {
        if (Status is not (SubscriptionStatus.Active or SubscriptionStatus.Trial or SubscriptionStatus.GracePeriod))
            return Result.Fail(ErrorCodes.SubscriptionInvalidTransition);
        if (untilUtc < now || untilUtc < CurrentPeriodStartUtc || untilUtc >= CurrentPeriodEndUtc ||
            _billingPeriods.Any(p => p.EndUtc > untilUtc))
            return Result.Fail(ErrorCodes.ValidationFailed, "cannot_shorten_billed_or_elapsed_period");
        CurrentPeriodEndUtc = untilUtc.ToUniversalTime();
        GraceEndsUtc = null;
        AppendEvent("subscription.reduced", now, actor, new { untilUtc = CurrentPeriodEndUtc });
        return Result.Ok();
    }

    public Result Suspend(string reason, DateTimeOffset now, ActorRef? actor = null)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 200) return Result.Fail(ErrorCodes.ValidationFailed);
        return Transition(SubscriptionStatus.Suspended, "subscription.suspended", now, actor, new { reason });
    }

    public Result Reinstate(DateTimeOffset now, ActorRef? actor = null)
    {
        if (Status != SubscriptionStatus.Suspended || CurrentPeriodEndUtc <= now)
            return Result.Fail(ErrorCodes.SubscriptionInvalidTransition, "renew_expired_period_instead");
        GraceEndsUtc = null;
        return Transition(SubscriptionStatus.Active, "subscription.reinstated", now, actor);
    }

    public Result Cancel(bool immediate, DateTimeOffset now, ActorRef? actor = null)
    {
        var allowed = SubscriptionLifecycle.EnsureAllowed(Status, SubscriptionStatus.Cancelled);
        if (allowed.IsFailure) return allowed;
        if (!immediate && Status is not (SubscriptionStatus.Trial or SubscriptionStatus.Active or SubscriptionStatus.GracePeriod))
            return Result.Fail(ErrorCodes.SubscriptionInvalidTransition);
        CancelRequestedUtc = now.ToUniversalTime();
        CancelAtPeriodEnd = !immediate;
        if (immediate) { Status = SubscriptionStatus.Cancelled; GraceEndsUtc = null; }
        AppendEvent(immediate ? "subscription.cancelled" : "subscription.cancel_requested", now, actor);
        return Result.Ok();
    }

    public Result Renew(DateTimeOffset now, Plan plan, ActorRef? actor = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var allowed = SubscriptionLifecycle.EnsureAllowed(Status, SubscriptionStatus.Active);
        if (allowed.IsFailure) return allowed;
        if (!plan.IsActive) return Result.Fail(ErrorCodes.PlanNotFound);
        var start = CurrentPeriodEndUtc > now ? CurrentPeriodEndUtc : now;
        PlanId = plan.Id;
        Status = SubscriptionStatus.Active;
        CurrentPeriodStartUtc = start.ToUniversalTime();
        CurrentPeriodEndUtc = CurrentPeriodStartUtc.AddMonths(1);
        GraceEndsUtc = null; CancelRequestedUtc = null; CancelAtPeriodEnd = false;
        AppendPeriod(plan);
        AppendEvent("subscription.renewed", now, actor, new { plan.Code, sequence = NextPeriodSequence - 1 });
        return Result.Ok();
    }

    public Result ChangeSeats(int limit, int activeSeatsUsed, DateTimeOffset now, ActorRef? actor = null)
    {
        if (Status == SubscriptionStatus.Cancelled) return Result.Fail(ErrorCodes.SubscriptionInvalidTransition);
        if (limit < 1 || activeSeatsUsed < 0) return Result.Fail(ErrorCodes.ValidationFailed);
        if (limit < activeSeatsUsed) return Result.Fail(ErrorCodes.SeatLimitExceeded);
        SeatLimit = limit;
        AppendEvent("subscription.seats_changed", now, actor, new { limit });
        return Result.Ok();
    }

    public Result ChangePlan(Plan plan, DateTimeOffset now, ActorRef? actor = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (Status == SubscriptionStatus.Cancelled) return Result.Fail(ErrorCodes.SubscriptionInvalidTransition);
        if (!plan.IsActive) return Result.Fail(ErrorCodes.PlanNotFound);
        PlanId = plan.Id;
        AppendEvent("subscription.plan_changed", now, actor, new { plan.Code });
        return Result.Ok();
    }

    public Result Tick(DateTimeOffset now, int graceDays = 7, ActorRef? actor = null)
    {
        if (graceDays is < 0 or > 365) return Result.Fail(ErrorCodes.ValidationFailed);
        if (CancelAtPeriodEnd && now >= CurrentPeriodEndUtc && Status != SubscriptionStatus.Cancelled)
            return Cancel(true, now, actor);
        if (Status is SubscriptionStatus.Trial or SubscriptionStatus.Active && now >= CurrentPeriodEndUtc)
        {
            GraceEndsUtc = CurrentPeriodEndUtc.AddDays(graceDays);
            var grace = Transition(SubscriptionStatus.GracePeriod, "subscription.grace_started", now, actor);
            if (grace.IsFailure) return grace;
        }
        if (Status == SubscriptionStatus.GracePeriod && GraceEndsUtc is { } end && now >= end)
            return Transition(SubscriptionStatus.Expired, "subscription.expired", now, actor);
        return Result.Ok();
    }

    private Result Transition(SubscriptionStatus to, string type, DateTimeOffset now, ActorRef? actor, object? payload = null)
    {
        var allowed = SubscriptionLifecycle.EnsureAllowed(Status, to);
        if (allowed.IsFailure) return allowed;
        Status = to;
        AppendEvent(type, now, actor, payload);
        return Result.Ok();
    }

    private void AppendPeriod(Plan plan) => _billingPeriods.Add(new BillingPeriod(Id, NextPeriodSequence++,
        CurrentPeriodStartUtc, CurrentPeriodEndUtc, plan.MonthlyAmountMinor, plan.Currency));

    private void AppendEvent(string type, DateTimeOffset now, ActorRef? actor, object? payload = null)
    {
        UpdatedUtc = now.ToUniversalTime();
        ConcurrencyStamp = Guid.NewGuid().ToString();
        _events.Add(new SubscriptionEvent(Id, NextEventSequence++, type, UpdatedUtc,
            actor?.Kind ?? ActorKind.System, actor?.ActorId, JsonSerializer.Serialize(payload ?? new { })));
    }
}
