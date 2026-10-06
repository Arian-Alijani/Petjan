using Animora.Domain.Finance;

namespace Animora.Application.Ports;

/// <summary>Write side of fixed (recurring) expenses and their per-period payment records.</summary>
public interface IRecurringExpenseRepository
{
    Task<RecurringExpense?> FindAsync(string id, CancellationToken ct = default);

    void Add(RecurringExpense entity);

    /// <summary>The payment that already settled <paramref name="periodCode"/> of the template, if any.</summary>
    Task<RecurringExpensePayment?> FindPaymentAsync(string recurringExpenseId, string periodCode, CancellationToken ct = default);

    void AddPayment(RecurringExpensePayment payment);
}
