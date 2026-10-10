using Animora.Application.Ports;
using Animora.Domain.Finance;
using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Repositories;

public sealed class RecurringExpenseRepository(AnimoraServerDbContext db) : TenantRepository<RecurringExpense>(db), IRecurringExpenseRepository
{
    public Task<RecurringExpense?> FindAsync(string id, CancellationToken ct = default) =>
        db.Set<RecurringExpense>().Local.FirstOrDefault(e => e.Id == id && !e.Deleted) is { } local
            ? Task.FromResult<RecurringExpense?>(local) : db.Set<RecurringExpense>().AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    public new void Add(RecurringExpense entity) => db.Set<RecurringExpense>().Add(entity);

    public Task<RecurringExpensePayment?> FindPaymentAsync(string recurringExpenseId, string periodCode, CancellationToken ct = default) =>
        db.Set<RecurringExpensePayment>().AsNoTracking()
            .FirstOrDefaultAsync(p => p.RecurringExpenseId == recurringExpenseId && p.PeriodCode == periodCode, ct);

    public void AddPayment(RecurringExpensePayment payment) => db.Set<RecurringExpensePayment>().Add(payment);
}
