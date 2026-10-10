using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;
namespace Animora.Persistence.PostgreSql.Replication;

public sealed class FeedEpochService(AnimoraServerDbContext context) : IFeedEpoch
{
    public async Task AdvanceAsync(string tenantId, string reason, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (!StringComparer.Ordinal.Equals(tenantId, context.CurrentTenantId))
            throw new InvalidOperationException("Feed epoch tenant mismatch.");
        var now = DateTimeOffset.UtcNow;
        int count = await context.Set<TenantFeedStateRow>().Where(x => x.TenantId == tenantId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Epoch, x => x.Epoch + 1)
                .SetProperty(x => x.Generation, x => x.Generation + 1)
                .SetProperty(x => x.OldestRetainedSequence, x => x.LastSequence)
                .SetProperty(x => x.UpdatedUtc, now), ct).ConfigureAwait(false);
        if (count != 1) throw new InvalidOperationException("Missing tenant feed state.");
    }
}
