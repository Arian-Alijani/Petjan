using Animora.Application.Commands;
using Animora.Domain.Results;
using Animora.Persistence.PostgreSql.Context;

namespace Animora.Persistence.PostgreSql.Repositories;

public sealed class LocalSequenceAllocator(AnimoraServerDbContext db, DocumentCounterAllocator counters) : ILocalSequenceAllocator
{
    public async Task<Result<long>> NextAsync(CancellationToken ct = default) =>
        Result<long>.Ok(await counters.NextAsync(db.CurrentTenantId, "cash-session", "all", ct));
}
