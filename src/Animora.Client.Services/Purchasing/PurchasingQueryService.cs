using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.UseCases.Purchasing;
using Animora.Application.UseCases.Purchasing.ReadModels;
using Animora.Client.Services.Inventory;
using Animora.Contracts.Purchasing;
using Animora.Domain.Time;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Client.Services.Purchasing;

/// <summary>
/// The real <see cref="IPurchasingQueryService"/> over the C06 snapshot query (mirrors the C01
/// <c>PatientsService</c> pattern). Singleton; the scoped dispatcher and the identity provider are resolved
/// from a fresh async scope per call and never captured. No EF or SQL here.
/// </summary>
public sealed class PurchasingQueryService(IServiceScopeFactory scopes) : IPurchasingQueryService
{
    public async Task<PurchasingSnapshot> GetSnapshotAsync()
    {
        await using var scope = scopes.CreateAsyncScope();
        var provider = scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>();
        var context = provider.BuildQueryContext();
        if (context.IsFailure) return Empty;

        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        var result = await dispatcher.QueryAsync(new PurchasingSnapshotQuery(), context.Value!);
        if (result.IsFailure) return Empty;

        var zone = scope.ServiceProvider.GetRequiredService<IClinicTimeZoneProvider>().GetClinicTimeZone();
        return Build(result.Value!, zone);
    }

    /// <summary>The snapshot rendered when the store is not readable yet — an empty, not-yet-loaded screen.</summary>
    private static PurchasingSnapshot Empty { get; } = new(
        new PurchaseKpi("۰", "سفارش فعال", string.Empty, false, "IconTruck", "brand"),
        new PurchaseKpi("۰", "خرید این ماه", string.Empty, false, "IconCreditCard", "info"),
        new PurchaseKpi("۰", "بدهی تأمین‌کننده", string.Empty, false, "IconDollar", "warn"),
        []);

    private static PurchasingSnapshot Build(PurchasingSnapshotReadModel row, TimeZoneInfo zone)
    {
        var orders = new List<PurchaseOrderSummary>(row.Orders.Count);
        for (var i = 0; i < row.Orders.Count; i++)
        {
            var order = row.Orders[i];
            if (StatusOf(order.State) is not { } status) continue; // Cancelled has no Contracts equivalent.
            orders.Add(new PurchaseOrderSummary(
                order.Id,
                PersianFormat.Digits(order.OrderNumber),
                order.SupplierName,
                Jalali(TimeZoneInfo.ConvertTimeFromUtc(order.OrderedUtc.UtcDateTime, zone)),
                PersianFormat.Count(order.LineCount) + " قلم",
                PersianFormat.Toman(order.GrandTotalMinor),
                SettlementOf(order.PaymentTermDays),
                status));
        }

        return new PurchasingSnapshot(
            new PurchaseKpi(PersianFormat.Count(row.ActiveOrderCount), "سفارش فعال",
                string.Empty, false, "IconTruck", "brand"),
            new PurchaseKpi(PersianFormat.CompactToman(row.MonthToDateSpendMinor), "خرید این ماه",
                string.Empty, false, "IconCreditCard", "info"),
            new PurchaseKpi(PersianFormat.CompactToman(row.SupplierDebtMinor), "بدهی تأمین‌کننده",
                string.Empty, false, "IconDollar", "warn"),
            orders);
    }

    /// <summary>
    /// Maps the domain lifecycle onto the three Contracts statuses the screen renders. A cancelled order has
    /// no equivalent, so it is omitted from the table (and only counted by the KPI block, which does not
    /// filter by state) rather than shown under a status it never reached.
    /// </summary>
    private static PurchaseOrderStatus? StatusOf(int state) => state switch
    {
        3 => PurchaseOrderStatus.Delivered,   // Received.
        5 => PurchaseOrderStatus.Finalized,   // Closed.
        4 => null,                            // Cancelled — not representable.
        _ => PurchaseOrderStatus.InProgress,  // Draft / Submitted / PartiallyReceived.
    };

    /// <summary>Settlement is derived from the supplier's agreed payment term (the domain's own fact).</summary>
    private static string SettlementOf(int paymentTermDays) => paymentTermDays <= 0 ? "نقدی" : "اعتباری";

    private static string Jalali(DateTime local)
    {
        var calendar = new System.Globalization.PersianCalendar();
        return $"{calendar.GetYear(local):0000}/{calendar.GetMonth(local):00}/{calendar.GetDayOfMonth(local):00}";
    }
}
