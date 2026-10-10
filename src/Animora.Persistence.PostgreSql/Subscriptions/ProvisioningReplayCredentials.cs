using Animora.Domain.Results;
using Animora.Domain.Subscriptions;
using Animora.Persistence.PostgreSql.Operator;
using Microsoft.EntityFrameworkCore;
namespace Animora.Persistence.PostgreSql.Subscriptions;

/// <summary>Recovers once-delivered credentials after an intake crash, without retaining plaintext in the ledger.</summary>
public sealed class ProvisioningReplayCredentials(AnimoraOperatorDbContext db, OwnerCredentialAdministration owners)
{
    public async Task<Result<string>> RecoverAsync(string tenantId, ActorRef actor, CancellationToken ct = default)
    {
        var stamp = await db.Subscriptions.AsNoTracking().Where(x => x.TenantId == tenantId).Select(x => x.ConcurrencyStamp)
            .SingleOrDefaultAsync(ct).ConfigureAwait(false);
        return stamp is null ? Result<string>.Fail(ErrorCodes.SubscriptionNotFound)
            : await owners.ResetAsync(tenantId, stamp, actor, ct).ConfigureAwait(false);
    }
}
