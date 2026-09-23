using Animora.Domain.Finance;

namespace Animora.Application.Ports;

public interface IExpenseRepository
{
    Task<Expense?> FindAsync(string id, CancellationToken ct = default);
    Task<Expense> RequireAsync(string id, CancellationToken ct = default);
    void Add(Expense entity);
    void AddRange(IEnumerable<Expense> entities);
}
