using Animora.Application.Ports;
using Animora.Domain.Finance;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Repositories;

public sealed class RecurringExpenseRepository(AnimoraLocalDbContext db) : IRecurringExpenseRepository
{
    public Task<RecurringExpense?> FindAsync(string id, CancellationToken ct = default) =>
        db.Set<RecurringExpense>().AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    public void Add(RecurringExpense entity) => db.Set<RecurringExpense>().Add(entity);

    public Task<RecurringExpensePayment?> FindPaymentAsync(string recurringExpenseId, string periodCode, CancellationToken ct = default) =>
        db.Set<RecurringExpensePayment>().AsNoTracking()
            .FirstOrDefaultAsync(p => p.RecurringExpenseId == recurringExpenseId && p.PeriodCode == periodCode, ct);

    public void AddPayment(RecurringExpensePayment payment) => db.Set<RecurringExpensePayment>().Add(payment);
}
