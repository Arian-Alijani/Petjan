using Animora.Application.Abstractions;
using Animora.Application.Security;
using Animora.Application.UseCases.Cashier;
using Animora.Application.UseCases.Sales;
using Animora.Application.UseCases.Sales.ReadModels;
using Animora.Contracts.Cashier;
using Animora.Domain.Results;
using Animora.Domain.Sales;
using Animora.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;
using DomainDiscount = Animora.Domain.Sales.DiscountKind;

namespace Animora.Client.Services.Cashier;

/// <summary>Thin C07 dispatcher adapter. No persistence or financial arithmetic lives in this layer.</summary>
public sealed class CashierService(IServiceScopeFactory scopes) : ICashierService
{
    private const int PageSize = 200;

    public async Task<IReadOnlyList<CashierInvoice>> GetInvoicesAsync()
    {
        var items = new List<CashierInvoice>();
        for (var page = 1; ; page++)
        {
            var result = await QueryAsync(new CashierInvoiceListQuery(Animora.Application.Ports.CashierInvoiceTab.All, page, PageSize));
            if (result.IsFailure || result.Value is null) break;
            foreach (var row in result.Value.Items) items.Add(row.ToContract(Math.Max(0, row.TotalMinor - row.DueMinor)));
            if (items.Count >= result.Value.TotalCount || result.Value.Items.Count == 0) break;
        }
        return items;
    }

    public async Task<CashierInvoiceDetail?> GetDetailAsync(string invoiceId)
    {
        var result = await QueryAsync(new InvoiceDetailQuery(invoiceId));
        return result.IsSuccess ? result.Value?.ToContract() : null;
    }

    public async Task<IReadOnlyList<CashierStat>> GetStatsAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var from = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var result = await QueryAsync(new CashierStatsQuery(from, from.AddDays(1)));
        if (result.IsFailure || result.Value is null) return [];
        var stats = result.Value;
        return [
            new CashierStat("warning", "wallet", "مطالبات باز", stats.OutstandingMinor, "", true),
            new CashierStat("success", "check", "دریافتی", stats.CollectedMinor, "", true),
            new CashierStat("info", "cash", "مغایرت صندوق", stats.SessionVarianceMinor, "", true),
            new CashierStat("neutral", "receipt", "تعداد فاکتور", stats.InvoiceCount, "", false),
        ];
    }

    public async Task<IReadOnlyList<CashierTabCount>> GetTabCountsAsync()
    {
        var result = await QueryAsync(new CashierTabCountsQuery());
        if (result.IsFailure || result.Value is null) return [];
        var c = result.Value;
        return [new(CashierTab.Open, "باز", c.Open), new(CashierTab.Settled, "تسویه‌شده", c.Settled),
            new(CashierTab.Void, "ابطال‌شده", c.Void), new(CashierTab.All, "همه", c.All)];
    }

    public async Task<IReadOnlyList<CashierInsurer>> GetInsurersAsync()
    {
        var result = await QueryAsync(new InsurersQuery());
        return result.IsSuccess && result.Value is not null
            ? result.Value.Select(i => new CashierInsurer(i.Id, i.Name, (int)i.CoveragePercent)).ToArray() : [];
    }

    public async Task<IReadOnlyList<CashierCatalogItem>> GetCatalogItemsAsync()
    {
        var result = await QueryAsync(new SalesCatalogQuery());
        return result.IsSuccess && result.Value is not null
            ? result.Value.Items.Select(i => new CashierCatalogItem(i.Id,
                i.Kind.Equals("Service", StringComparison.OrdinalIgnoreCase) ? CashierLineKind.Service : CashierLineKind.Item,
                i.Title, i.PriceMinor)).ToArray() : [];
    }

    public async Task<CashierActionResult> PayAsync(string invoiceId, CashierPayRequest request)
    {
        var current = await GetDetailAsync(invoiceId);
        if (current is null) return new(false, "INVOICE_NOT_FOUND", null);
        if (current.State == CashierInvoiceState.Draft)
        {
            var post = await SendAsync(new PostInvoiceCommand(invoiceId));
            if (post.IsFailure) return new(false, post.Error.Code, current);
        }
        var method = request.Method switch
        {
            CashierPaymentMethod.Cash => PaymentMethod.Cash,
            CashierPaymentMethod.Card => PaymentMethod.Card,
            CashierPaymentMethod.Cheque => PaymentMethod.Cheque,
            CashierPaymentMethod.Credit => PaymentMethod.Credit,
            _ => PaymentMethod.Transfer,
        };
        var paid = await SendAsync(new RegisterPaymentCommand(invoiceId, method, Money.Irt(request.Amount), request.Reference));
        return new(paid.IsSuccess, paid.IsFailure ? paid.Error.Code : null, await GetDetailAsync(invoiceId));
    }

    public async Task<CashierActionResult> VoidAsync(string invoiceId)
    {
        var result = await SendAsync(new VoidInvoiceCommand(invoiceId, "ابطال در صندوق"));
        return new(result.IsSuccess, result.IsFailure ? result.Error.Code : null, await GetDetailAsync(invoiceId));
    }

    public async Task<CashierActionResult> RefundAsync(string invoiceId, CashierRefundRequest request)
    {
        var result = await SendAsync(new RefundInvoiceCommand(invoiceId, Money.Irt(request.Amount), "بازپرداخت در صندوق"));
        return new(result.IsSuccess, result.IsFailure ? result.Error.Code : null, await GetDetailAsync(invoiceId));
    }

    public async Task<CashierInvoiceDetail> UpdateLineAsync(string invoiceId, int lineIndex, int quantity, int discountPercent)
    {
        var current = await RequireDetailAsync(invoiceId);
        if (lineIndex < 0 || lineIndex >= current.Lines.Count) throw new ArgumentOutOfRangeException(nameof(lineIndex));
        var model = await QueryAsync(new InvoiceDetailQuery(invoiceId));
        var lineId = model.Value!.Lines[lineIndex].LineId;
        Ensure(await SendAsync(new EditInvoiceLineCommand(invoiceId, lineIndex, lineId, quantity, discountPercent)));
        return await RequireDetailAsync(invoiceId);
    }

    public async Task<CashierInvoiceDetail> RemoveLineAsync(string invoiceId, int lineIndex)
    {
        var model = await QueryAsync(new InvoiceDetailQuery(invoiceId));
        if (model.IsFailure || model.Value is null || lineIndex < 0 || lineIndex >= model.Value.Lines.Count)
            throw new ArgumentOutOfRangeException(nameof(lineIndex));
        Ensure(await SendAsync(new RemoveInvoiceLineCommand(invoiceId, lineIndex, model.Value.Lines[lineIndex].LineId)));
        return await RequireDetailAsync(invoiceId);
    }

    public async Task<CashierInvoiceDetail> AddLineAsync(string invoiceId, string catalogItemId)
    {
        Ensure(await SendAsync(new AddInvoiceLineCommand(invoiceId, catalogItemId, 1)));
        return await RequireDetailAsync(invoiceId);
    }

    public async Task<CashierInvoiceDetail> SetDiscountAsync(string invoiceId, CashierDiscountKind kind, int value)
    {
        Ensure(await SendAsync(new SetInvoiceDiscountCommand(invoiceId,
            kind == CashierDiscountKind.Amount ? DomainDiscount.Amount : DomainDiscount.Percent, value)));
        return await RequireDetailAsync(invoiceId);
    }

    public async Task<CashierInvoiceDetail> SetInsuranceAsync(string invoiceId, string? insurerId)
    {
        Ensure(await SendAsync(new SetInvoiceInsuranceCommand(invoiceId, insurerId)));
        return await RequireDetailAsync(invoiceId);
    }

    public Task<CashierInvoiceDetail> RecalculateAsync(string invoiceId) => RequireDetailAsync(invoiceId);

    private async Task<CashierInvoiceDetail> RequireDetailAsync(string id) =>
        await GetDetailAsync(id) ?? throw new InvalidOperationException("INVOICE_NOT_FOUND");

    private static void Ensure(Result<Unit> result)
    {
        if (result.IsFailure) throw new InvalidOperationException(result.Error.Code);
    }

    private async Task<Result<T>> QueryAsync<T>(IQuery<T> query)
    {
        await using var scope = scopes.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        var context = scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>().BuildQueryContext();
        return context.IsFailure ? Result<T>.Fail(context.Error, context.Detail)
            : await dispatcher.QueryAsync(query, context.Value!);
    }

    private async Task<Result<T>> SendAsync<T>(ICommand<T> command)
    {
        await using var scope = scopes.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        var context = await scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>()
            .BuildCommandContextAsync(null, null, null, default);
        return context.IsFailure ? Result<T>.Fail(context.Error, context.Detail)
            : await dispatcher.SendAsync(command, context.Value!);
    }
}
