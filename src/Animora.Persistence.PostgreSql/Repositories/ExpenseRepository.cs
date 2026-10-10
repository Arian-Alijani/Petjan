using Animora.Application.Ports;
using Animora.Domain.Finance;
using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Repositories;

public sealed class ExpenseRepository(AnimoraServerDbContext db) : TenantRepository<Expense>(db), IExpenseRepository
{
    public Task<Expense?> FindAsync(string id, CancellationToken ct = default) =>
        db.Set<Expense>().Local.FirstOrDefault(e => e.Id == id && !e.Deleted) is { } local
            ? Task.FromResult<Expense?>(local) : db.Set<Expense>().AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<Expense> RequireAsync(string id, CancellationToken ct = default) =>
        await FindAsync(id, ct) ?? throw new InvalidOperationException($"Expense {id} not found.");

    public new void Add(Expense entity) => db.Set<Expense>().Add(entity);
    public void AddRange(IEnumerable<Expense> entities) => db.Set<Expense>().AddRange(entities);
}
