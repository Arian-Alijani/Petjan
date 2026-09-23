using Animora.Application.Ports;
using Animora.Domain.Finance;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Repositories;

public sealed class PeriodRepository(AnimoraLocalDbContext db) : IPeriodRepository
{
    public Task<AccountingPeriod?> FindAsync(string id, CancellationToken ct = default) =>
        db.Set<AccountingPeriod>().AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<AccountingPeriod> RequireAsync(string id, CancellationToken ct = default) =>
        await FindAsync(id, ct) ?? throw new InvalidOperationException($"AccountingPeriod {id} not found.");

    public void Add(AccountingPeriod entity) => db.Set<AccountingPeriod>().Add(entity);
    public void AddRange(IEnumerable<AccountingPeriod> entities) => db.Set<AccountingPeriod>().AddRange(entities);
}
