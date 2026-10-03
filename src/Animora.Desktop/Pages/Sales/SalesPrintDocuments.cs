using Animora.Contracts.Sales;
using Animora.Desktop.Services;

namespace Animora.Desktop.Pages.Sales;

/// <summary>
/// Print-ready HTML for the Sales screen (invoice, payment receipt, day report). Pure formatting over contract
/// data via <see cref="PrintDocumentBuilder"/>; the ViewModels only hand the result to <see cref="IPrintService"/>.
/// </summary>
public static class SalesPrintDocuments
{
    private const string Unit = " تومان";

    /// <summary>The invoice (or, when <paramref name="receiptPayment"/> is set, its payment receipt).</summary>
    public static string Invoice(InvoiceDetail d, string fontCss, string? receiptTitle = null, long? receiptPayment = null)
    {
        var meta = new List<PrintMeta>
        {
            new("شمارهٔ فاکتور", Digits(d.Id)),
            new("وضعیت", d.StateLabel),
            new("مشتری", d.IsGuest || d.OwnerName.Length == 0 ? "مشتری گذری" : d.OwnerName),
            new("تلفن", string.IsNullOrWhiteSpace(d.Phone) ? "—" : d.Phone),
        };
        if (d.PetName.Length > 0)
        {
            meta.Add(new PrintMeta("حیوان", d.PetName));
        }

        if (!string.IsNullOrEmpty(d.ReceiptNo))
        {
            meta.Add(new PrintMeta("شمارهٔ رسید", Digits(d.ReceiptNo)));
        }

        if (d.HasInsurance && d.InsuranceLabel.Length > 0)
        {
            meta.Add(new PrintMeta("بیمه", d.InsuranceLabel));
        }

        var rows = d.Lines.Select(l => (IReadOnlyList<string>)new[]
        {
            SalePresentation.Fa(l.Index + 1),
            l.Title,
            l.KindLabel,
            SalePresentation.Money(l.UnitPrice),
            SalePresentation.Fa(l.Quantity),
            SalePresentation.Fa(l.DiscountPercent) + "٪",
            SalePresentation.Money(l.Subtotal),
        }).ToList();

        var totals = new List<PrintTotal> { new("جمع اقلام", SalePresentation.Money(d.Base) + Unit) };
        if (d.HasDiscount)
        {
            totals.Add(new PrintTotal("تخفیف", "− " + SalePresentation.Money(d.DiscountAmount) + Unit));
        }

        if (d.Tax > 0)
        {
            totals.Add(new PrintTotal($"مالیات ({SalePresentation.Fa(d.TaxPercent)}٪)", "+ " + SalePresentation.Money(d.Tax) + Unit));
        }

        if (d.HasInsurance)
        {
            totals.Add(new PrintTotal("سهم بیمه", "− " + SalePresentation.Money(d.InsuranceShare) + Unit));
        }

        totals.Add(new PrintTotal("مبلغ نهایی", SalePresentation.Money(d.Total) + Unit, true));
        foreach (var p in d.Payments)
        {
            totals.Add(new PrintTotal($"{(p.Amount < 0 ? "عودت" : "پرداخت")} {p.MethodLabel} · {p.At}", SalePresentation.Money(p.Amount) + Unit));
        }

        if (receiptPayment is { } paid)
        {
            totals.Add(new PrintTotal("مبلغ این پرداخت", SalePresentation.Money(paid) + Unit, true));
        }

        totals.Add(new PrintTotal("پرداخت‌شده", SalePresentation.Money(d.Paid) + Unit));
        totals.Add(new PrintTotal("مانده", SalePresentation.Money(d.Due) + Unit, receiptPayment is null));

        return PrintDocumentBuilder.Build(
            receiptTitle ?? "فاکتور فروش",
            d.PetName.Length > 0 ? $"{d.OwnerName} · {d.PetName}" : d.OwnerName,
            meta,
            ["#", "شرح", "نوع", "قیمت واحد", "تعداد", "تخفیف", "مبلغ ردیف"],
            rows,
            totals,
            fontCss);
    }

    /// <summary>The day report: the loaded invoice list plus the page's headline KPIs.</summary>
    public static string DayReport(IReadOnlyList<InvoiceSummary> invoices, SaleKpiSnapshot kpi, string fontCss)
    {
        var rows = invoices.Select(i => (IReadOnlyList<string>)new[]
        {
            Digits(i.Id),
            i.OwnerName.Length == 0 ? "مشتری گذری" : i.OwnerName,
            i.PetName.Length == 0 ? "—" : i.PetName,
            SalePresentation.StateLabel(i.State),
            SalePresentation.Money(i.Total),
            SalePresentation.Money(i.Due),
        }).ToList();

        return PrintDocumentBuilder.Build(
            "گزارش فروش و صندوق",
            "وضعیت فاکتورها در لحظهٔ تهیهٔ گزارش",
            [
                new PrintMeta("تاریخ", Digits(Animora.Desktop.Localization.JalaliCalendar.Today)),
                new PrintMeta("تعداد فاکتور", SalePresentation.Fa(invoices.Count)),
            ],
            ["شماره", "مشتری", "حیوان", "وضعیت", "مبلغ کل", "مانده"],
            rows,
            [
                new PrintTotal("دریافتی امروز", SalePresentation.Money(kpi.SettledTodayTotal) + Unit),
                new PrintTotal("مانده فاکتورهای باز", SalePresentation.Money(kpi.OpenDueTotal) + Unit, true),
                new PrintTotal("فاکتورهای باز", SalePresentation.Fa(kpi.OpenCount)),
            ],
            fontCss);
    }

    private static string Digits(string text) => Animora.Desktop.Localization.PersianNumberFormatter.ToPersianDigits(text);
}
