using Animora.Application.Ports;
using Animora.Domain.Finance;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Repositories;

public sealed class ExpenseRepository(AnimoraLocalDbContext db) : IExpenseRepository
{
    public Task<Expense?> FindAsync(string id, CancellationToken ct = default) =>
        db.Set<Expense>().AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<Expense> RequireAsync(string id, CancellationToken ct = default) =>
        await FindAsync(id, ct) ?? throw new InvalidOperationException($"Expense {id} not found.");

    public void Add(Expense entity) => db.Set<Expense>().Add(entity);
    public void AddRange(IEnumerable<Expense> entities) => db.Set<Expense>().AddRange(entities);
}
