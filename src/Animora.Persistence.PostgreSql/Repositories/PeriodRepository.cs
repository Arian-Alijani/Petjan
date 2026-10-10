using Animora.Application.Ports;
using Animora.Domain.Finance;
using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Repositories;

public sealed class PeriodRepository(AnimoraServerDbContext db) : TenantRepository<AccountingPeriod>(db), IPeriodRepository
{
    public Task<AccountingPeriod?> FindAsync(string id, CancellationToken ct = default) =>
        db.Set<AccountingPeriod>().Local.FirstOrDefault(e => e.Id == id && !e.Deleted) is { } local
            ? Task.FromResult<AccountingPeriod?>(local) : db.Set<AccountingPeriod>().AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<AccountingPeriod> RequireAsync(string id, CancellationToken ct = default) =>
        await FindAsync(id, ct) ?? throw new InvalidOperationException($"AccountingPeriod {id} not found.");

    public Task<AccountingPeriod?> FindByCodeAsync(string periodCode, CancellationToken ct = default) =>
        db.Set<AccountingPeriod>().AsTracking().FirstOrDefaultAsync(e => e.PeriodCode == periodCode, ct);

    public Task<AccountingPeriod?> FindContainingAsync(DateTimeOffset occurredUtc, CancellationToken ct = default) =>
        db.Set<AccountingPeriod>().AsTracking()
            .FirstOrDefaultAsync(e => e.StartUtc <= occurredUtc && e.EndUtc > occurredUtc, ct);

    public Task<AccountingPeriod?> FindNextOpenAsync(DateTimeOffset afterUtc, CancellationToken ct = default) =>
        db.Set<AccountingPeriod>().AsTracking().Where(e => e.StartUtc >= afterUtc && e.State == PeriodState.Open)
            .OrderBy(e => e.StartUtc).FirstOrDefaultAsync(ct);

    public new void Add(AccountingPeriod entity) => db.Set<AccountingPeriod>().Add(entity);
    public void AddRange(IEnumerable<AccountingPeriod> entities) => db.Set<AccountingPeriod>().AddRange(entities);
}
