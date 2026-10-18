using Animora.Contracts.Purchasing;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Animora.Desktop.Pages.Inventory;

/// <summary>
/// One row of the «تأمین‌کنندگان» register: the supplier's identity (name, code, phone) on the first line and its
/// purchase relationship on the next — how many invoices were recorded, how much was bought in total, when the last
/// purchase happened and whether an order is still open. Built once, so the table only binds.
/// </summary>
public sealed partial class SupplierRegisterRowViewModel : ObservableObject
{
    public SupplierRegisterRowViewModel(SupplierSummary summary)
    {
        Summary = summary;
        Id = summary.Id;
        Name = summary.Name;
        CodeText = string.IsNullOrWhiteSpace(summary.Code) ? "—" : summary.Code;
        PhoneText = string.IsNullOrWhiteSpace(summary.Phone) ? "بدون شماره" : summary.Phone;
        HasPhone = !string.IsNullOrWhiteSpace(summary.Phone);

        ReceiptsText = PersianNumberFormatter.ToPersianDigits(summary.ReceiptCount);
        TotalText = InventoryPresentation.Money(summary.TotalPurchased);
        LastPurchaseText = string.IsNullOrWhiteSpace(summary.LastPurchaseJalali)
            ? "بدون خرید"
            : JalaliCalendar.Short(summary.LastPurchaseJalali);

        OpenOrderCountText = PersianNumberFormatter.ToPersianDigits(summary.OpenOrderCount);
        HasOpenOrders = summary.OpenOrderCount > 0;

        TermText = summary.PaymentTermDays <= 0
            ? "نقدی"
            : PersianNumberFormatter.ToPersianDigits(summary.PaymentTermDays) + " روز";
    }

    public SupplierSummary Summary { get; }

    public string Id { get; }

    public string Name { get; }

    public string CodeText { get; }

    public string PhoneText { get; }

    public bool HasPhone { get; }

    public string ReceiptsText { get; }

    public string TotalText { get; }

    /// <summary>The last purchase as a Persian-digit Jalali date, or «بدون خرید».</summary>
    public string LastPurchaseText { get; }

    public string OpenOrderCountText { get; }

    public bool HasOpenOrders { get; }

    /// <summary>The agreed payment term («نقدی» when it is zero).</summary>
    public string TermText { get; }

    /// <summary>Selected row highlight (master-detail: the profile panel shows this supplier).</summary>
    [ObservableProperty]
    private bool _isActive;
}

/// <summary>
/// One completed purchase in a supplier's history: the receipt number, its Jalali date, the line count, the total
/// and the supplier's own invoice reference (when one was recorded).
/// </summary>
public sealed class SupplierPurchaseRowViewModel
{
    public SupplierPurchaseRowViewModel(SupplierPurchaseRecord record)
    {
        Number = string.IsNullOrWhiteSpace(record.Number)
            ? "—"
            : PersianNumberFormatter.ToPersianDigits(record.Number);
        DateText = string.IsNullOrWhiteSpace(record.DateJalali)
            ? "—"
            : JalaliCalendar.Short(record.DateJalali);
        LineCountText = PersianNumberFormatter.ToPersianDigits(record.LineCount);
        TotalText = InventoryPresentation.Money(record.Total);
        ReferenceText = string.IsNullOrWhiteSpace(record.InvoiceReference) ? "—" : record.InvoiceReference;
    }

    public string Number { get; }

    public string DateText { get; }

    public string LineCountText { get; }

    public string TotalText { get; }

    public string ReferenceText { get; }
}
