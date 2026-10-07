using System.Globalization;
using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.Text;
using Animora.Application.UseCases.Purchasing;
using Animora.Application.UseCases.Purchasing.ReadModels;
using Animora.Contracts.Purchasing;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Client.Services.Purchasing;

/// <summary>
/// The real <see cref="IPurchasingService"/> over the C06 purchasing use cases (same pattern as
/// <c>InventoryQueryService</c>): singleton, a fresh async scope per call resolving the scoped dispatcher and
/// identity provider, no EF/SQL and no business rule here. The purchase price is set by the receipt handler in the same
/// unit of work as the stock movements, so a recorded invoice is atomic.
/// </summary>
public sealed class PurchasingService(IServiceScopeFactory scopes) : IPurchasingService
{
    private const int ListSize = 100;
    private const int PickerSize = 50;

    public async Task<PurchasingOverview> GetOverviewAsync()
    {
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider, zones) = Resolve(scope);
        var context = provider.BuildQueryContext();
        if (context.IsFailure) throw new ServiceException(context.Error);

        var snapshot = await dispatcher.QueryAsync(new PurchasingSnapshotQuery(), context.Value!);
        if (snapshot.IsFailure) throw new ServiceException(snapshot.Error);
        var receipts = await dispatcher.QueryAsync(new PurchaseReceiptListQuery(ListSize), context.Value!);
        if (receipts.IsFailure) throw new ServiceException(receipts.Error);

        var zone = zones.GetClinicTimeZone();
        var rows = receipts.Value!.Receipts;
        var items = new List<PurchaseListItem>(rows.Count);
        for (var i = 0; i < rows.Count; i++) items.Add(ToItem(rows[i], zone));

        var s = snapshot.Value!;
        return new PurchasingOverview(
            s.ActiveOrderCount, s.MonthToDateSpendMinor, s.SupplierDebtMinor,
            receipts.Value.ReceiptCount, receipts.Value.SupplierCount, items);
    }

    public async Task<IReadOnlyList<SupplierRef>> FindSuppliersAsync(string? search)
    {
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider, _) = Resolve(scope);
        var context = provider.BuildQueryContext();
        if (context.IsFailure) throw new ServiceException(context.Error);

        var term = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var result = await dispatcher.QueryAsync(new SupplierDirectoryQuery(term, PickerSize), context.Value!);
        if (result.IsFailure) throw new ServiceException(result.Error);

        var rows = result.Value!;
        var list = new List<SupplierRef>(rows.Count);
        for (var i = 0; i < rows.Count; i++) list.Add(ToRef(rows[i]));
        return list;
    }

    public async Task<SupplierRef> CreateSupplierAsync(string name)
    {
        var id = await SendAsync(new CreateSupplierCommand(name ?? string.Empty));
        var found = await FindSuppliersAsync(name);
        return found.FirstOrDefault(s => s.Id == id) ?? new SupplierRef(id, string.Empty, (name ?? string.Empty).Trim(), null, 0);
    }

    public async Task<PurchaseRecordResult> RecordPurchaseAsync(RecordPurchaseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Lines.Count == 0) return new PurchaseRecordResult(false, ErrorCodes.ValidationFailed.Code, string.Empty);

        try
        {
            var supplierId = request.SupplierId;
            if (string.IsNullOrWhiteSpace(supplierId))
            {
                if (string.IsNullOrWhiteSpace(request.NewSupplierName))
                    return new PurchaseRecordResult(false, ErrorCodes.ValidationFailed.Code, string.Empty);
                supplierId = await SendAsync(new CreateSupplierCommand(request.NewSupplierName));
            }

            await using var scope = scopes.CreateAsyncScope();
            var (dispatcher, provider, zones) = Resolve(scope);
            var zone = zones.GetClinicTimeZone();
            var lines = new GoodsReceiptLineInput[request.Lines.Count];
            for (var i = 0; i < lines.Length; i++)
            {
                var line = request.Lines[i];
                if (line.Quantity <= 0 || line.UnitCost < 0)
                    return new PurchaseRecordResult(false, ErrorCodes.ValidationFailed.Code, string.Empty);

                DateTimeOffset? expiry = null;
                if (!string.IsNullOrWhiteSpace(line.ExpiryJalali))
                {
                    expiry = ClinicDayWindow.ParseJalali(line.ExpiryJalali, zone, endOfDay: false);
                    if (expiry is null) return new PurchaseRecordResult(false, ErrorCodes.ValidationFailed.Code, string.Empty);
                }

                lines[i] = new GoodsReceiptLineInput(line.ProductId, line.VariantId, line.Quantity * 1000L, line.UnitCost,
                    string.IsNullOrWhiteSpace(line.Lot) ? null : line.Lot.Trim(), expiry);
            }

            var context = await provider.BuildCommandContextAsync(null, null, null, default);
            if (context.IsFailure) return new PurchaseRecordResult(false, context.Error.Code, string.Empty);

            var reference = string.IsNullOrWhiteSpace(request.InvoiceReference)
                ? null
                : PersianText.NormalizeDigits(request.InvoiceReference.Trim());
            var result = await dispatcher.SendAsync(
                new ReceiveGoodsCommand(supplierId!, lines, null, reference), context.Value!);
            return result.IsFailure
                ? new PurchaseRecordResult(false, result.Error.Code, string.Empty)
                : new PurchaseRecordResult(true, null, result.Value!);
        }
        catch (ServiceException ex)
        {
            return new PurchaseRecordResult(false, ex.Code, string.Empty);
        }
    }

    private static SupplierRef ToRef(SupplierRowReadModel row) =>
        new(row.Id, row.Code, row.Name, row.Phone, row.PaymentTermDays);

    private static PurchaseListItem ToItem(GoodsReceiptRowReadModel row, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(row.ReceivedUtc.UtcDateTime, zone);
        var calendar = new PersianCalendar();
        var date = $"{calendar.GetYear(local):0000}/{calendar.GetMonth(local):00}/{calendar.GetDayOfMonth(local):00}";
        var state = row.State switch
        {
            1 => "ثبت‌شده",
            2 => "برگشت‌خورده",
            _ => "پیش‌نویس",
        };
        return new PurchaseListItem(row.Id, row.ReceiptNumber, row.SupplierName, date, row.LineCount, row.TotalMinor,
            row.InvoiceReference ?? string.Empty, state);
    }

    private async Task<T> SendAsync<T>(ICommand<T> command)
    {
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider, _) = Resolve(scope);
        var context = await provider.BuildCommandContextAsync(null, null, null, default);
        if (context.IsFailure) throw new ServiceException(context.Error);
        var result = await dispatcher.SendAsync(command, context.Value!);
        if (result.IsFailure) throw new ServiceException(result.Error);
        return result.Value!;
    }

    private static (IDispatcher Dispatcher, ICurrentUserProvider Provider, IClinicTimeZoneProvider Zones) Resolve(
        AsyncServiceScope scope) =>
        (scope.ServiceProvider.GetRequiredService<IDispatcher>(),
         scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>(),
         scope.ServiceProvider.GetRequiredService<IClinicTimeZoneProvider>());
}
