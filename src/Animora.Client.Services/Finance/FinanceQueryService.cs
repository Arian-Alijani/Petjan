using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.UseCases.Finance;
using Animora.Contracts.Finance;
using Animora.Domain.Sales;
using Animora.Domain.Time;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Client.Services.Finance;

/// <summary>Singleton finance seam: each dispatch uses its own scope and the projected fact queries.</summary>
public sealed class FinanceQueryService(IServiceScopeFactory scopes) : IFinanceQueryService
{
    public async Task<FinanceOverview> GetOverviewAsync(string range)
    {
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider, _) = Resolve(scope);
        var context = provider.BuildQueryContext();
        if (context.IsFailure) return FinanceMapping.EmptyOverview;
        var result = await dispatcher.QueryAsync(new FinanceOverviewQuery(range), context.Value!);
        return result.IsSuccess ? result.Value!.ToContract() : FinanceMapping.EmptyOverview;
    }
    public async Task<FinanceIncome> GetIncomeAsync(string range)
    {
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider, _) = Resolve(scope);
        var context = provider.BuildQueryContext();
        if (context.IsFailure) return FinanceMapping.EmptyIncome;
        var result = await dispatcher.QueryAsync(new FinanceIncomeQuery(range), context.Value!);
        return result.IsSuccess ? result.Value!.ToContract() : FinanceMapping.EmptyIncome;
    }
    public async Task<FinanceExpense> GetExpenseAsync(string range)
    {
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider, zones) = Resolve(scope);
        var context = provider.BuildQueryContext();
        if (context.IsFailure) return FinanceMapping.EmptyExpense;
        var result = await dispatcher.QueryAsync(new FinanceExpenseQuery(range), context.Value!);
        return result.IsSuccess ? result.Value!.ToContract(zones.GetClinicTimeZone()) : FinanceMapping.EmptyExpense;
    }
    public async Task<FinanceProfit> GetProfitAsync(string range)
    {
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider, _) = Resolve(scope);
        var context = provider.BuildQueryContext();
        if (context.IsFailure) return FinanceMapping.EmptyProfit;
        var result = await dispatcher.QueryAsync(new FinanceProfitQuery(range), context.Value!);
        return result.IsSuccess ? result.Value!.ToContract() : FinanceMapping.EmptyProfit;
    }
    public async Task<IReadOnlyList<FinanceRange>> GetRangesAsync()
    {
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider, _) = Resolve(scope);
        var context = provider.BuildQueryContext();
        if (context.IsFailure) return [];
        var result = await dispatcher.QueryAsync(new FinanceRangeCatalogQuery(), context.Value!);
        return result.IsSuccess ? result.Value!.Where(r => r.Id is "7d" or "30d" or "90d" or "ytd")
            .Select(r => new FinanceRange(r.Id, r.Label, r.Days, r.Bucket)).ToArray() : [];
    }
    public Task<IReadOnlyList<FinanceProductSort>> GetProductSortsAsync() =>
        Task.FromResult(FinanceMapping.ProductSorts);
    public async Task<IReadOnlyList<FinanceCategoryOption>> GetExpenseCategoriesAsync()
    {
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider, _) = Resolve(scope);
        var context = provider.BuildQueryContext();
        if (context.IsFailure) return [];
        var result = await dispatcher.QueryAsync(new FinanceExpenseCategoriesQuery(), context.Value!);
        return result.IsSuccess ? result.Value!.Select(FinanceMapping.CategoryOption)
            .DistinctBy(c => c.Id).ToArray() : [];
    }
    public async Task<IReadOnlyList<FinancePaymentMethod>> GetPaymentMethodsAsync()
    {
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider, _) = Resolve(scope);
        var context = provider.BuildQueryContext();
        if (context.IsFailure) return [];
        var result = await dispatcher.QueryAsync(new FinancePaymentMethodsQuery(), context.Value!);
        return result.IsSuccess ? result.Value!.Select(FinanceMapping.MethodOption).ToArray() : [];
    }
    public async Task<ExpenseEntry> AddExpenseAsync(NewExpenseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        // The Contracts form date is a display-only Jalali string; the posting timestamp is the clinic clock.
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider, _) = Resolve(scope);
        var context = await provider.BuildCommandContextAsync(null, null, null, default);
        if (context.IsFailure) throw new ServiceException(context.Error);
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var result = await dispatcher.SendAsync(new RecordExpenseCommand(
            FinanceMapping.CategoryCode(request.CategoryId), request.Description, request.Amount,
            clock.UtcNow, FinanceMapping.ParseMethod(request.MethodId)), context.Value!);
        if (result.IsFailure) throw new ServiceException(result.Error);
        return new ExpenseEntry(result.Value!, request.Description, request.CategoryId, request.MethodId,
            request.Amount, request.Date, context.Value!.UserId);
    }
    private static (IDispatcher Dispatcher, ICurrentUserProvider Provider, IClinicTimeZoneProvider Zones) Resolve(AsyncServiceScope scope) =>
        (scope.ServiceProvider.GetRequiredService<IDispatcher>(),
         scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>(),
         scope.ServiceProvider.GetRequiredService<IClinicTimeZoneProvider>());
}
