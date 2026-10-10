using Animora.Domain.Results;
using Animora.Domain.Subscriptions;
using Animora.Domain.Time;
using Animora.Persistence.PostgreSql.Operator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Animora.Persistence.PostgreSql.Subscriptions;

public sealed class SubscriptionService(AnimoraOperatorDbContext context, ISeatUsageReader seats,
    ITenantAdministration tenants, ILicenseIssuanceScheduler scheduler, IClock clock,
    IOptions<SubscriptionOptions> options) : ISubscriptionService
{
    public async Task<Result<Subscription>> MutateAsync(string tenantId, string expectedStamp, SubscriptionMutation mutation,
        ActorRef actor, CancellationToken ct = default)
    {
        await using var transaction = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(ct).ConfigureAwait(false) : null;
        var s = await context.Subscriptions.Include(x => x.BillingPeriods)
            .SingleOrDefaultAsync(x => x.TenantId == tenantId, ct).ConfigureAwait(false);
        if (s is null) return Result<Subscription>.Fail(ErrorCodes.SubscriptionNotFound);
        if (!string.Equals(s.ConcurrencyStamp, expectedStamp.Trim('"'), StringComparison.Ordinal))
            return Result<Subscription>.Fail(ErrorCodes.ConcurrencyConflict);
        var now = clock.UtcNow;
        var plan = mutation.Operation is "renew" or "plan"
            ? await context.Plans.SingleOrDefaultAsync(x => mutation.PlanCode == null ? x.Id == s.PlanId : x.Code == mutation.PlanCode, ct).ConfigureAwait(false)
            : null;
        Result result = mutation.Operation switch
        {
            "extend" when mutation.UntilUtc is { } until => s.Extend(until, now, actor),
            "extend" when mutation.Days is { } days => s.Extend(days, now, actor),
            "reduce" when mutation.UntilUtc is { } until => s.Reduce(until, now, actor),
            "suspend" => s.Suspend(mutation.Reason ?? string.Empty, now, actor),
            "reinstate" => s.Reinstate(now, actor),
            "cancel" => s.Cancel(mutation.Immediate, now, actor),
            "renew" when plan != null => s.Renew(now, plan, actor),
            "plan" when plan != null => s.ChangePlan(plan, now, actor),
            "renew" or "plan" => Result.Fail(ErrorCodes.PlanNotFound),
            "seats" when mutation.Seats is { } limit => s.ChangeSeats(limit, await seats.CountAsync(tenantId, ct).ConfigureAwait(false), now, actor),
            "tick" when actor.Kind == ActorKind.System => s.Tick(now, options.Value.GraceDays, actor),
            _ => Result.Fail(ErrorCodes.ValidationFailed),
        };
        if (result.IsFailure) { context.ChangeTracker.Clear(); return Result<Subscription>.Fail(result.Error, result.Detail); }
        var status = await tenants.SetStatusAsync(tenantId, SubscriptionTenantStatusMap.Map(s.Status), mutation.Operation, actor, ct).ConfigureAwait(false);
        if (status.IsFailure) { context.ChangeTracker.Clear(); return Result<Subscription>.Fail(status.Error, status.Detail); }
        context.Audit.Add(OperatorAudit.Create(now, actor, "subscription." + mutation.Operation, tenantId, s.Id));
        await scheduler.ScheduleAsync(tenantId, s.Id, ct).ConfigureAwait(false);
        try
        {
            await context.SaveChangesAsync(ct).ConfigureAwait(false);
            if (transaction is not null) await transaction.CommitAsync(ct).ConfigureAwait(false);
            return Result<Subscription>.Ok(s);
        }
        catch (DbUpdateConcurrencyException)
        {
            context.ChangeTracker.Clear();
            return Result<Subscription>.Fail(ErrorCodes.ConcurrencyConflict);
        }
    }
}
