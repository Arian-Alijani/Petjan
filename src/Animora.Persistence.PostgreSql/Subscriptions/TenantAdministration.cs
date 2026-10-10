using Animora.Domain.Identity;
using Animora.Domain.Results;
using Animora.Domain.Subscriptions;
using Animora.Domain.Time;
using Animora.Persistence.PostgreSql.Operator;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Subscriptions;

public interface ITenantAdministration
{
    Task<Result> SetStatusAsync(string tenantId, TenantStatus status, string reason, ActorRef actor, CancellationToken ct = default);
}
public sealed class TenantAdministration(AnimoraOperatorDbContext context, IClock clock) : ITenantAdministration
{
    public async Task<Result> SetStatusAsync(string tenantId, TenantStatus status, string reason, ActorRef actor, CancellationToken ct = default)
    {
        if (context.Database.CurrentTransaction is null) throw new InvalidOperationException("Tenant status requires the caller's transaction.");
        var tenant = await context.Tenants.SingleOrDefaultAsync(x => x.Id == tenantId && !x.Deleted, ct).ConfigureAwait(false);
        if (tenant is null) return Result.Fail(ErrorCodes.NotFound);
        tenant.SetStatus(status);
        context.Audit.Add(OperatorAudit.Create(clock.UtcNow, actor, "tenant.status_changed", tenantId,
            tenantId, detail: new { status, reason }));
        return Result.Ok();
    }
}
