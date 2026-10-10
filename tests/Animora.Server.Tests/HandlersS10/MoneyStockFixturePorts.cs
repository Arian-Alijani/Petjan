using Animora.Application.Ports;
using Animora.Domain.Inventory;
using Animora.Domain.Purchasing;
using Animora.Domain.Finance;
namespace Animora.Server.Tests.HandlersS10;
internal sealed class FixtureProductCategoryRepository : IProductCategoryRepository
{
    public List<ProductCategory> Rows { get; } = [];
    public Task<ProductCategory?> FindAsync(string id, CancellationToken ct = default) => Task.FromResult(Rows.FirstOrDefault(e => e.Id == id && !e.Deleted));
    public void Add(ProductCategory entity) => Rows.Add(entity);
    public Task<bool> NameExistsAsync(string name, string? excludeId, CancellationToken ct = default) => Task.FromResult(Rows.Any(e => !e.Deleted && e.Id != excludeId && e.DisplayName == name));
    public Task<int> CountProductsAsync(string categoryId, CancellationToken ct = default) => Task.FromResult(0);
    public Task<int> NextSortOrderAsync(CancellationToken ct = default) => Task.FromResult(Rows.Count);
}
internal sealed class FixtureChequeRepository : IChequeRepository
{
    public List<Cheque> Rows { get; } = [];
    public Task<Cheque?> FindAsync(string id, CancellationToken ct = default) => Task.FromResult(Rows.FirstOrDefault(e => e.Id == id && !e.Deleted));
    public void Add(Cheque entity) => Rows.Add(entity);
    public Task<Cheque> RequireAsync(string id, CancellationToken ct = default) => Task.FromResult(Rows.Single(e => e.Id == id));
    public void AddRange(IEnumerable<Cheque> entities) => Rows.AddRange(entities);
}
internal sealed class FixtureExpenseRepository : IExpenseRepository
{
    public List<Expense> Rows { get; } = [];
    public Task<Expense?> FindAsync(string id, CancellationToken ct = default) => Task.FromResult(Rows.FirstOrDefault(e => e.Id == id && !e.Deleted));
    public void Add(Expense entity) => Rows.Add(entity);
    public Task<Expense> RequireAsync(string id, CancellationToken ct = default) => Task.FromResult(Rows.Single(e => e.Id == id));
    public void AddRange(IEnumerable<Expense> entities) => Rows.AddRange(entities);
}
internal sealed class FixtureRecurringExpenseRepository : IRecurringExpenseRepository
{
    public List<RecurringExpense> Rows { get; } = [];
    public Task<RecurringExpense?> FindAsync(string id, CancellationToken ct = default) => Task.FromResult(Rows.FirstOrDefault(e => e.Id == id && !e.Deleted));
    public void Add(RecurringExpense entity) => Rows.Add(entity);
    public List<RecurringExpensePayment> Payments { get; } = [];
    public Task<RecurringExpensePayment?> FindPaymentAsync(string recurringExpenseId, string periodCode, CancellationToken ct = default) =>
        Task.FromResult(Payments.FirstOrDefault(p => p.RecurringExpenseId == recurringExpenseId && p.PeriodCode == periodCode));
    public void AddPayment(RecurringExpensePayment payment) => Payments.Add(payment);
}
internal sealed class FixtureSuppliers : ISupplierRepository
{
    public List<Supplier> Rows { get; } = [];
    public Task<Supplier?> FindByNameAsync(string name, CancellationToken ct = default) => Task.FromResult(Rows.FirstOrDefault(s => s.Name == name));
    public void Add(Supplier supplier) => Rows.Add(supplier);
}
internal sealed class FixtureExpenseNumbers : IExpenseNumberAllocator
{
    private long _next;
    public Task<string> AllocateAsync(string tenantId, DateTimeOffset issuedUtc, CancellationToken ct = default) => Task.FromResult($"EXP-05-{++_next:D6}");
}
