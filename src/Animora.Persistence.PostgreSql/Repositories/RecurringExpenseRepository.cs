using Animora.Application.Ports;
using Animora.Domain.Finance;
using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Repositories;

public sealed class RecurringExpenseRepository(AnimoraServerDbContext db) : TenantRepository<RecurringExpense>(db), IRecurringExpenseRepository
{
    public Task<RecurringExpense?> FindAsync(string id, CancellationToken ct = default) =>
        Context.Set<RecurringExpense>().Local.FirstOrDefault(e => e.Id == id && !e.Deleted) is { } local
            ? Task.FromResult<RecurringExpense?>(local) : Context.Set<RecurringExpense>().AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    public new void Add(RecurringExpense entity) => Context.Set<RecurringExpense>().Add(entity);

    public Task<RecurringExpensePayment?> FindPaymentAsync(string recurringExpenseId, string periodCode, CancellationToken ct = default) =>
        Context.Set<RecurringExpensePayment>().AsNoTracking()
            .FirstOrDefaultAsync(p => p.RecurringExpenseId == recurringExpenseId && p.PeriodCode == periodCode, ct);

    public void AddPayment(RecurringExpensePayment payment) => Context.Set<RecurringExpensePayment>().Add(payment);
}
