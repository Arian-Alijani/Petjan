using Animora.Application.Abstractions;
using Animora.Application.Security;
using Animora.Application.UseCases.Finance.Cheques;
using Animora.Contracts.Cheques;
using Animora.Domain.Finance;
using Animora.Domain.Results;
using Microsoft.Extensions.DependencyInjection;
using ContractCheque = Animora.Contracts.Cheques.Cheque;
using ContractDirection = Animora.Contracts.Cheques.ChequeDirection;
using DomainDirection = Animora.Domain.Finance.ChequeDirection;

namespace Animora.Client.Services.Cheques;

/// <summary>Scope-per-call adapter over the cheque use cases; the bank list is a static domain catalog.</summary>
public sealed class ChequesQueryService(IServiceScopeFactory scopes) : IChequesQueryService
{
    public async Task<IReadOnlyList<ContractCheque>> GetAllAsync()
    {
        var items = new List<ContractCheque>();
        for (var page = 1; ; page++)
        {
            var result = await QueryAsync(new ChequeListQuery(page, 200));
            if (result.IsFailure || result.Value is null) break;
            items.AddRange(result.Value.Items.Select(ChequesMapping.ToContract));
            if (items.Count >= result.Value.TotalCount || result.Value.Items.Count == 0) break;
        }
        return items;
    }

    public Task<IReadOnlyList<ChequeBank>> GetBanksAsync() => Task.FromResult<IReadOnlyList<ChequeBank>>(
        BankDirectory.All.Select(b => new ChequeBank(b.Key, b.Name, b.Short, b.ColorFrom, b.ColorTo)).ToArray());

    public async Task<CashflowSummary> GetCashflowAsync()
    {
        var result = await QueryAsync(new ChequeCashflowQuery());
        return new CashflowSummary(result.IsSuccess && result.Value is not null
            ? result.Value.Select(w => new CashflowWeek(ChequesMapping.Date(
                new DateTimeOffset(w.WeekStart.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)),
                w.IncomingMinor, w.OutgoingMinor)).ToArray() : []);
    }

    public async Task<IReadOnlyList<BankSummary>> GetBankBreakdownAsync()
    {
        var result = await QueryAsync(new ChequeBankBreakdownQuery());
        if (result.IsFailure || result.Value is null) return [];
        return result.Value.Select(row =>
        {
            var bank = BankDirectory.All.FirstOrDefault(b => b.Name == row.BankKey || b.Key == row.BankKey);
            return new BankSummary(bank?.Key ?? row.BankKey, bank?.Name ?? row.BankKey,
                bank?.Short ?? row.BankKey, bank?.ColorFrom ?? "#738393", bank?.ColorTo ?? "#465767",
                row.AmountMinor, row.Count);
        }).ToArray();
    }

    public async Task<IReadOnlyList<ChequeAlert>> GetAlertsAsync()
    {
        var result = await QueryAsync(new ChequeAlertsQuery());
        if (result.IsFailure || result.Value is null) return [];
        return result.Value.Select(row => new ChequeAlert(row.Kind switch
        {
            "Bounced" => ChequeAlertKind.Bounced,
            "Overdue" => ChequeAlertKind.Overdue,
            _ => ChequeAlertKind.Today,
        }, row.Kind switch
        {
            "Bounced" => "چک برگشتی",
            "Overdue" => "چک سررسید گذشته",
            _ => "سررسید نزدیک",
        }, $"{row.AmountMinor:N0} — {row.DueDay:yyyy/MM/dd}")).ToArray();
    }

    public async Task<ContractCheque> CreateAsync(NewChequeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var bank = BankDirectory.All.FirstOrDefault(b => b.Key == request.BankKey);
        var result = await SendAsync(new CreateChequeCommand(request.Number,
            request.Direction == ContractDirection.In ? DomainDirection.Inbound : DomainDirection.Outbound,
            request.Amount, ChequesMapping.ParseDate(request.DueDate), ChequesMapping.ParseDate(request.IssueDate),
            OwnerId: request.Direction == ContractDirection.In ? request.Party : null,
            SupplierId: request.Direction == ContractDirection.Out ? request.Party : null,
            BankName: bank?.Name ?? request.BankKey, BankBranch: request.Branch, AccountNumber: request.Account,
            SayadId: request.Sayad, Note: request.Note));
        if (result.IsFailure) throw new ServiceException(result.Error);
        return (await GetAllAsync()).First(c => c.Id == result.Value);
    }

    public async Task UpdateAsync(string chequeId, UpdateChequeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var bank = BankDirectory.All.FirstOrDefault(b => b.Key == request.BankKey);
        Ensure(await SendAsync(new UpdateChequeCommand(chequeId, request.Number,
            ChequesMapping.ParseDate(request.DueDate), bank?.Name ?? request.BankKey,
            request.Branch, request.Account, request.Sayad, request.Note)));
    }

    public async Task DeleteAsync(string chequeId) => Ensure(await SendAsync(new DeleteChequeCommand(chequeId)));

    public async Task BulkUpdateStatusAsync(IReadOnlyList<string> ids, ChequeStatus newStatus)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var target = ChequesMapping.Target(newStatus);
        // Registered -> InPortfolio -> Deposited: each is a separate journaled business operation.
        if (target == ChequeState.Deposited)
        {
            var current = await GetAllAsync();
            foreach (var id in ids.Where(id => current.Any(c => c.Id == id && c.Status == ChequeStatus.InHand)))
            {
                // The domain rejects an already-in-portfolio cheque; only advance a registered one.
                var moved = await SendAsync(new ChangeChequeStatusCommand(id, ChequeState.InPortfolio));
                if (moved.IsFailure && moved.Error != ErrorCodes.ChequeInvalidTransition)
                    throw new ServiceException(moved.Error);
            }
        }
        var result = await SendAsync(new BulkChangeChequeStatusCommand(ids, target,
            target is ChequeState.Bounced or ChequeState.Returned or ChequeState.Cancelled ? "ثبت تغییر وضعیت" : null));
        if (result.IsFailure) throw new ServiceException(result.Error);
        var failed = result.Value!.Where(r => !r.Succeeded).ToArray();
        if (failed.Length != 0)
            throw new InvalidOperationException(string.Join(", ", failed.Select(r => $"{r.ChequeId}: {r.Error}")));
    }

    private static void Ensure(Result<Unit> result)
    {
        if (result.IsFailure) throw new ServiceException(result.Error);
    }

    private async Task<Result<T>> QueryAsync<T>(IQuery<T> query)
    {
        await using var scope = scopes.CreateAsyncScope();
        var provider = scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>();
        var context = provider.BuildQueryContext();
        return context.IsFailure ? Result<T>.Fail(context.Error, context.Detail)
            : await scope.ServiceProvider.GetRequiredService<IDispatcher>().QueryAsync(query, context.Value!);
    }

    private async Task<Result<T>> SendAsync<T>(ICommand<T> command)
    {
        await using var scope = scopes.CreateAsyncScope();
        var provider = scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>();
        var context = await provider.BuildCommandContextAsync(null, null, null, default);
        return context.IsFailure ? Result<T>.Fail(context.Error, context.Detail)
            : await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(command, context.Value!);
    }
}
