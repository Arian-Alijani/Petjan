using Animora.Persistence.PostgreSql.Operator;
using Microsoft.EntityFrameworkCore;
namespace Animora.Persistence.PostgreSql.Provisioning;
/// <summary>Safe intake summaries only: no request payload, result body, or temporary credential.</summary>
public sealed class ProvisioningQueries(IProvisioningIntentStore store, ProvisioningDbContext db)
{
    public async Task<OperatorPage<IntentListItem>> ListAsync(ProvisioningIntentStatus? status, int page, int size, CancellationToken ct = default)
    {
        page = Math.Clamp(page, 1, int.MaxValue / 200); size = Math.Clamp(size, 1, 200);
        long total = await db.Intents.AsNoTracking().Where(x => status == null || x.Status == status).LongCountAsync(ct).ConfigureAwait(false);
        return new(await store.ListAsync(status, page, size, ct).ConfigureAwait(false), page, size, total);
    }
    public Task<IntentListItem?> DetailAsync(string id, CancellationToken ct = default) => db.Intents.AsNoTracking().Where(x => x.Id == id)
        .Select(x => new IntentListItem(x.Id, x.IdempotencyKey, x.ClientId, x.Status, x.Attempts, x.ErrorCode, x.ReceivedUtc, x.CompletedUtc))
        .SingleOrDefaultAsync(ct);
}
