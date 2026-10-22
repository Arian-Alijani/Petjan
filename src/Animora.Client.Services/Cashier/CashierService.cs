using Animora.Application.Abstractions;
using Animora.Application.Security;
using Animora.Application.UseCases.Cashier;
using Animora.Application.UseCases.Sales;
using Animora.Application.UseCases.Sales.ReadModels;
using Animora.Client.Services.Payments;
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

    // «فاکتورهای اخیر» is a fixed window of the five most-recently-issued invoices — never paged.
    private const int RecentInvoiceCount = 5;

    public async Task<CashierInvoicePage> GetInvoicesAsync(CashierTab tab, string? search, int page, int pageSize)
    {
        var applicationTab = tab switch
        {
            CashierTab.Open => Animora.Application.Ports.CashierInvoiceTab.Open,
            CashierTab.Settled => Animora.Application.Ports.CashierInvoiceTab.Settled,
            CashierTab.Void => Animora.Application.Ports.CashierInvoiceTab.Void,
            _ => Animora.Application.Ports.CashierInvoiceTab.All,
        };

        // The «فاکتورهای اخیر» tab always shows exactly the first five rows (newest first) and never pages.
        if (tab == CashierTab.Settled)
        {
            var recent = await QueryAsync(new CashierInvoiceListQuery(applicationTab, 1, RecentInvoiceCount,
                string.IsNullOrWhiteSpace(search) ? null : search.Trim()));
            if (recent.IsFailure || recent.Value is null) return new CashierInvoicePage([], 0, 1, RecentInvoiceCount);
            var recentRows = recent.Value.Items;
            var recentItems = new List<CashierInvoice>(recentRows.Count);
            for (var i = 0; i < recentRows.Count; i++)
                recentItems.Add(recentRows[i].ToContract(Math.Max(0, recentRows[i].TotalMinor - recentRows[i].DueMinor)));
            // Report TotalCount == the rows shown so the «نمایش بیشتر» affordance stays hidden.
            return new CashierInvoicePage(recentItems, recentItems.Count, 1, RecentInvoiceCount);
        }

        var size = Math.Clamp(pageSize, 1, PageSize);
        var result = await QueryAsync(new CashierInvoiceListQuery(applicationTab, Math.Max(1, page), size,
            string.IsNullOrWhiteSpace(search) ? null : search.Trim()));
        if (result.IsFailure || result.Value is null) return new CashierInvoicePage([], 0, Math.Max(1, page), size);

        var rows = result.Value.Items;
        var items = new List<CashierInvoice>(rows.Count);
        for (var i = 0; i < rows.Count; i++) items.Add(rows[i].ToContract(Math.Max(0, rows[i].TotalMinor - rows[i].DueMinor)));
        return new CashierInvoicePage(items, result.Value.TotalCount, result.Value.Page, size);
    }

    public async Task<CashierInvoiceDetail?> GetDetailAsync(string invoiceId)
    {
        var result = await QueryAsync(new InvoiceDetailQuery(invoiceId));
        return result.IsSuccess ? result.Value?.ToContract() : null;
    }

    public async Task<IReadOnlyList<CashierStat>> GetStatsAsync()
    {
        // Same clinic-local day window the Sales KPIs use, so both screens agree on "today".
        var (from, to) = ClinicDayWindow.Today(scopes);
        var result = await QueryAsync(new CashierStatsQuery(from, to));
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
        // The «فاکتورهای اخیر» tab is a fixed window of the five newest invoices, so its badge counts the rows it shows.
        return [new(CashierTab.Open, "باز", c.Open), new(CashierTab.Settled, "فاکتورهای اخیر", Math.Min(c.All, RecentInvoiceCount)),
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
                i.Title, i.PriceMinor, i.OnHandScaled)).ToArray() : [];
    }

    public async Task<CashierActionResult> PayAsync(string invoiceId, CashierPayRequest request)
    {
        // One shared pay-on-post / cap-to-due path for Sales and Cashier (X41).
        var outcome = await InvoicePaymentCore.PayAsync(invoiceId, InvoicePaymentCore.ToDomain(request.Method),
            request.Amount, request.Reference, command => SendAsync(command), () => ReadModelAsync(invoiceId),
            () => CashSessionAutoOpen.EnsureOpenAsync(scopes));
        return new(outcome.Ok, outcome.Error, await GetDetailAsync(invoiceId), outcome.Applied);
    }

    public async Task<CashierActionResult> VoidAsync(string invoiceId)
    {
        var result = await SendAsync(new VoidInvoiceCommand(invoiceId, "ابطال در صندوق"));
        return new(result.IsSuccess, result.IsFailure ? result.Error.Code : null, await GetDetailAsync(invoiceId));
    }

    public async Task<CashierActionResult> RefundAsync(string invoiceId, CashierRefundRequest request)
    {
        var outcome = await InvoicePaymentCore.RefundAsync(invoiceId, request.Amount, "بازپرداخت در صندوق",
            command => SendAsync(command), () => ReadModelAsync(invoiceId));
        return new(outcome.Ok, outcome.Error, await GetDetailAsync(invoiceId), outcome.Applied);
    }

    // Addressed by the LineId the user saw (X27): a concurrent insert/remove makes the domain answer
    // INVOICE_LINE_INDEX_STALE instead of silently editing whichever line now sits at that index.
    public async Task<CashierInvoiceDetail> UpdateLineAsync(
        string invoiceId, int lineIndex, string lineId, int quantity, int discountPercent)
    {
        Ensure(await SendAsync(new EditInvoiceLineCommand(invoiceId, lineIndex, lineId, quantity, discountPercent)));
        return await RequireDetailAsync(invoiceId);
    }

    public async Task<CashierInvoiceDetail> RemoveLineAsync(string invoiceId, int lineIndex, string lineId)
    {
        Ensure(await SendAsync(new RemoveInvoiceLineCommand(invoiceId, lineIndex, lineId)));
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

    private async Task<InvoiceDetailReadModel?> ReadModelAsync(string invoiceId)
    {
        var result = await QueryAsync(new InvoiceDetailQuery(invoiceId));
        return result.IsSuccess ? result.Value : null;
    }

    private async Task<CashierInvoiceDetail> RequireDetailAsync(string id) =>
        await GetDetailAsync(id) ?? throw new InvalidOperationException("INVOICE_NOT_FOUND");

    private static void Ensure(Result<Unit> result)
    {
        if (result.IsFailure) throw new ServiceException(result.Error);
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
