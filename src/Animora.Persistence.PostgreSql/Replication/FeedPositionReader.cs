using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;
namespace Animora.Persistence.PostgreSql.Replication;

public sealed class FeedPositionReader(AnimoraServerDbContext context) : IFeedPosition
{
    public Task<FeedPosition> GetAsync(string tenantId, CancellationToken ct = default) =>
        context.Set<TenantFeedStateRow>().AsNoTracking().Where(x => x.TenantId == tenantId)
            .Select(x => new FeedPosition(x.Epoch, x.Generation, x.LastSequence, x.OldestRetainedSequence)).SingleAsync(ct);
}
