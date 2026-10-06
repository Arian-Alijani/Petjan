using Animora.Application.Abstractions;
using Animora.Application.Correlation;
using Animora.Application.Security;
using Animora.Application.UseCases.Finance;
using Animora.Contracts.Statistics;
using Dto = Animora.Contracts.Statistics;
using Animora.Domain.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Animora.Client.Services.Statistics;

/// <summary>
/// Dispatcher adapter for the «آمار» screen: one scope per call, one dispatcher query/command per method, Application
/// read models mapped 1:1 to Contracts DTOs. No persistence, permission or financial logic lives here — the Application
/// pipeline authorizes, validates and journals. Failures surface as <see cref="StatisticsQueryException"/>.
/// </summary>
public sealed class StatisticsQueryService(IServiceScopeFactory scopes) : IStatisticsQueryService
{
    private const int MaxPageSize = 200;
    private const int MaxTake = 50;

    // ── Overview / revenue ───────────────────────────────────────────────────────────
    public Task<StatisticsOverview> GetOverviewAsync(StatisticsRange range, CancellationToken ct = default) =>
        QueryAsync(new StatisticsOverviewQuery(range.ToWindow()), r => r.ToContract(), ct);

    public Task<IReadOnlyList<Dto.StatisticsSeriesPoint>> GetRevenueSeriesAsync(StatisticsRange range, CancellationToken ct = default) =>
        QueryAsync(new StatisticsRevenueSeriesQuery(range.ToWindow()),
            r => (IReadOnlyList<Dto.StatisticsSeriesPoint>)r.Select(p => p.ToContract()).ToArray(), ct);

    public Task<IReadOnlyList<RevenueSourceSlice>> GetRevenueBySourceAsync(StatisticsRange range, CancellationToken ct = default) =>
        QueryAsync(new StatisticsRevenueBySourceQuery(range.ToWindow()),
            r => (IReadOnlyList<RevenueSourceSlice>)r.Select(s => s.ToContract()).ToArray(), ct);

    public Task<IReadOnlyList<TopProduct>> GetTopProductsAsync(StatisticsRange range, int take, CancellationToken ct = default) =>
        QueryAsync(new TopProductsQuery(range.ToWindow(), Math.Clamp(take, 1, MaxTake)),
            r => (IReadOnlyList<TopProduct>)r.Select(p => p.ToContract()).ToArray(), ct);

    // ── Clients ──────────────────────────────────────────────────────────────────────
    public Task<IReadOnlyList<TopClient>> GetTopClientsAsync(int take, CancellationToken ct = default) =>
        QueryAsync(new TopClientsQuery(Math.Clamp(take, 1, MaxTake)),
            r => (IReadOnlyList<TopClient>)r.Select(c => c.ToContract()).ToArray(), ct);

    public Task<IReadOnlyList<SpeciesSlice>> GetSpeciesDistributionAsync(CancellationToken ct = default) =>
        QueryAsync(new SpeciesDistributionQuery(),
            r => (IReadOnlyList<SpeciesSlice>)r.Select(s => s.ToContract()).ToArray(), ct);

    public Task<ClientStats> GetClientStatsAsync(StatisticsRange range, CancellationToken ct = default) =>
        QueryAsync(new ClientStatsQuery(range.ToWindow()), r => r.ToContract(), ct);

    // ── Expenses ─────────────────────────────────────────────────────────────────────
    public Task<ExpensePage> GetExpensesAsync(StatisticsRange range, string? categoryCode, int page, int pageSize,
        CancellationToken ct = default) =>
        QueryAsync(new ExpenseListQuery(range.ToWindow(), string.IsNullOrWhiteSpace(categoryCode) ? null : categoryCode.Trim(),
                Math.Max(1, page), Math.Clamp(pageSize, 1, MaxPageSize)),
            r => new ExpensePage(r.Items.Select(i => i.ToContract()).ToArray(), r.TotalCount, r.Page, r.PageSize), ct);

    public Task<ExpenseStats> GetExpenseStatsAsync(StatisticsRange range, CancellationToken ct = default) =>
        QueryAsync(new ExpenseStatsQuery(range.ToWindow()), r => r.ToContract(), ct);

    public Task<string> AddExpenseAsync(NewExpense e, CancellationToken ct = default) =>
        SendAsync(new AddExpenseCommand(e.CategoryCode, e.Title, e.AmountMinor, e.IncurredUtc, e.Settlement.ToDomain(),
            e.SupplierId, e.ReceiptFileId, e.Note), ct);

    public Task<string> ReverseExpenseAsync(string expenseId, string reason, CancellationToken ct = default) =>
        SendAsync(new ReverseExpenseCommand(expenseId, reason), ct);

    // ── Fixed (recurring) expenses ───────────────────────────────────────────────────
    public Task<RecurringExpenseList> GetRecurringExpensesAsync(CancellationToken ct = default) =>
        QueryAsync(new RecurringExpensesQuery(), r => r.ToContract(), ct);

    public Task<string> AddRecurringExpenseAsync(RecurringExpenseInput i, CancellationToken ct = default) =>
        SendAsync(new AddRecurringExpenseCommand(i.CategoryCode, i.Title, i.AmountMinor, i.DayOfMonth, i.SupplierId, i.Note), ct);

    public Task<string> EditRecurringExpenseAsync(string recurringExpenseId, RecurringExpenseInput i, CancellationToken ct = default) =>
        SendAsync(new EditRecurringExpenseCommand(recurringExpenseId, i.CategoryCode, i.Title, i.AmountMinor, i.DayOfMonth,
            i.SupplierId, i.Note), ct);

    public Task SetRecurringExpenseActiveAsync(string recurringExpenseId, bool isActive, CancellationToken ct = default) =>
        SendAsync(new ToggleRecurringExpenseCommand(recurringExpenseId, isActive), ct);

    public Task<string> PayRecurringExpenseAsync(string recurringExpenseId, ExpenseSettlement settlement = ExpenseSettlement.Cash,
        CancellationToken ct = default) =>
        SendAsync(new PayRecurringExpenseForPeriodCommand(recurringExpenseId, settlement.ToDomain()), ct);

    // ── Dispatch plumbing ────────────────────────────────────────────────────────────
    // Microsoft.Data.Sqlite runs async reads synchronously: keep the whole scoped call off the caller (UI) thread.
    private Task<TOut> QueryAsync<TRead, TOut>(IQuery<TRead> query, Func<TRead, TOut> map, CancellationToken ct) =>
        Task.Run(async () =>
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var context = scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>().BuildQueryContext();
                if (context.IsFailure) throw Failure(scope.ServiceProvider, context.Error.Code);
                var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>()
                    .QueryAsync(query, context.Value!, ct).ConfigureAwait(false);
                if (result.IsFailure) throw Failure(scope.ServiceProvider, result.Error.Code);
                return map(result.Value!);
            }
            catch (OperationCanceledException) { throw; }
            catch (StatisticsQueryException) { throw; }
            catch (Exception ex) { throw Unexpected(ex); }
        }, ct);

    private Task<string> SendAsync(ICommand<string> command, CancellationToken ct) =>
        Task.Run(async () =>
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var context = await scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>()
                    .BuildCommandContextAsync(null, null, null, ct).ConfigureAwait(false);
                if (context.IsFailure) throw Failure(scope.ServiceProvider, context.Error.Code);
                var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>()
                    .SendAsync(command, context.Value!, ct).ConfigureAwait(false);
                if (result.IsFailure) throw Failure(scope.ServiceProvider, result.Error.Code);
                return result.Value!;
            }
            catch (OperationCanceledException) { throw; }
            catch (StatisticsQueryException) { throw; }
            catch (Exception ex) { throw Unexpected(ex); }
        }, ct);

    private StatisticsQueryException Unexpected(Exception ex)
    {
        using var scope = scopes.CreateScope();
        return Failure(scope.ServiceProvider, ErrorMapping.FromException(ex).Code);
    }

    private static StatisticsQueryException Failure(IServiceProvider services, string code)
    {
        var kind = StatisticsQueryException.Classify(code);
        var correlation = services.GetService<ICorrelationContext>()?.CurrentId ?? Guid.NewGuid().ToString("N");
        // Whitelisted category + code + opaque correlation only: no exception text, payload, identity or SQL in this log.
        services.GetService<ILogger<StatisticsQueryService>>()?.LogWarning(
            "Statistics call failed: {FailureKind} {ErrorCode}; correlation {CorrelationId}", kind, code, correlation);
        return new StatisticsQueryException(kind, code, correlation);
    }
}
