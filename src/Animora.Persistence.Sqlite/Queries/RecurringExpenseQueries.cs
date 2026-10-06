using Animora.Application.Ports;
using Animora.Domain.Finance;
using Animora.Domain.Purchasing;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Queries;

/// <summary>Bounded read of the (few) fixed expenses joined with the given period's payment and supplier name.</summary>
public sealed class RecurringExpenseQueries(AnimoraLocalDbContext db) : IRecurringExpenseQueries
{
    public async Task<IReadOnlyList<RecurringExpenseRow>> GetForPeriodAsync(string periodCode, CancellationToken ct)
    {
        var query = from r in db.Set<RecurringExpense>().AsNoTracking()
            join p in db.Set<RecurringExpensePayment>().AsNoTracking().Where(x => x.PeriodCode == periodCode)
                on r.Id equals p.RecurringExpenseId into payments
            from p in payments.DefaultIfEmpty()
            join s in db.Set<Supplier>().AsNoTracking() on r.SupplierId equals s.Id into suppliers
            from s in suppliers.DefaultIfEmpty()
            orderby r.IsActive descending, r.DayOfMonth, r.Title, r.Id
            select new RecurringExpenseRow(r.Id, r.CategoryCode, r.Title, r.Amount.Minor, r.DayOfMonth, r.IsActive,
                r.SupplierId, s == null ? null : s.Name, r.Note,
                p == null ? null : p.ExpenseId, p == null ? (DateTimeOffset?)null : p.PaidUtc);
        return await query.ToListAsync(ct);
    }
}
