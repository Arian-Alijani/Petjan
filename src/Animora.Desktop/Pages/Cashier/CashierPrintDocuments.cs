using System.Globalization;
using System.Text;
using Animora.Contracts.Cashier;
using Animora.Desktop.Services;

namespace Animora.Desktop.Pages.Cashier;

/// <summary>Print / export documents of the Cashier screen — pure formatting over contract data (X13).</summary>
public static class CashierPrintDocuments
{
    private const string Unit = " تومان";

    public static string Invoice(CashierInvoiceDetail d, string fontCss)
    {
        var meta = new List<PrintMeta>
        {
            new("شمارهٔ فاکتور", Digits(d.Id)),
            new("وضعیت", d.StateLabel),
            new("سرپرست", d.OwnerName.Length == 0 ? "—" : d.OwnerName),
            new("تلفن", d.Phone.Length == 0 ? "—" : d.Phone),
            new("تاریخ", d.Date),
        };
        if (d.PetName.Length > 0)
        {
            meta.Add(new PrintMeta("حیوان", d.PetName));
        }

        var rows = d.Lines.Select(l => (IReadOnlyList<string>)new[]
        {
            CashierPresentation.Fa(l.Index + 1),
            l.Title,
            CashierPresentation.LineKindLabel(l.Kind),
            CashierPresentation.Money(l.UnitPrice),
            CashierPresentation.Fa(l.Quantity),
            CashierPresentation.Fa(l.DiscountPercent) + "٪",
            CashierPresentation.Money(l.Subtotal),
        }).ToList();

        var totals = new List<PrintTotal> { new("جمع اقلام", CashierPresentation.Money(d.Base) + Unit) };
        if (d.DiscountAmount > 0)
        {
            totals.Add(new PrintTotal("تخفیف", "− " + CashierPresentation.Money(d.DiscountAmount) + Unit));
        }

        if (d.Tax > 0)
        {
            totals.Add(new PrintTotal($"مالیات ({CashierPresentation.Fa(d.TaxPercent)}٪)", "+ " + CashierPresentation.Money(d.Tax) + Unit));
        }

        if (d.InsuranceShare > 0)
        {
            totals.Add(new PrintTotal("سهم بیمه", "− " + CashierPresentation.Money(d.InsuranceShare) + Unit));
        }

        totals.Add(new PrintTotal("مبلغ نهایی", CashierPresentation.Money(d.Total) + Unit, true));
        foreach (var p in d.Payments)
        {
            totals.Add(new PrintTotal(
                $"{(p.Amount < 0 ? "عودت" : "پرداخت")} {p.MethodLabel} · {p.At}", CashierPresentation.Money(p.Amount) + Unit));
        }

        totals.Add(new PrintTotal("پرداخت‌شده", CashierPresentation.Money(d.Paid) + Unit));
        totals.Add(new PrintTotal("مانده", CashierPresentation.Money(d.Due) + Unit, true));

        return PrintDocumentBuilder.Build(
            "فاکتور",
            d.PetName.Length > 0 ? $"{d.OwnerName} · {d.PetName}" : d.OwnerName,
            meta,
            ["#", "شرح", "نوع", "قیمت واحد", "تعداد", "تخفیف", "مبلغ ردیف"],
            rows,
            totals,
            fontCss);
    }

    /// <summary>
    /// The listed invoices as CSV (UTF-8; the save flow prepends a BOM so Excel reads Persian text). Fields are
    /// quoted and a leading <c>= + - @</c> is neutralised so a customer name cannot run as a spreadsheet formula.
    /// </summary>
    public static string InvoicesCsv(IReadOnlyList<CashierInvoice> invoices)
    {
        var sb = new StringBuilder();
        sb.Append("شماره,وضعیت,سرپرست,حیوان,تاریخ,مبلغ کل,پرداخت‌شده,مانده\r\n");
        foreach (var i in invoices)
        {
            sb.Append(Csv(i.Id)).Append(',')
              .Append(Csv(i.StateLabel)).Append(',')
              .Append(Csv(i.OwnerName)).Append(',')
              .Append(Csv(i.PetName)).Append(',')
              .Append(Csv(i.Date)).Append(',')
              .Append(i.Total.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(i.Paid.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(i.Due.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        }

        return sb.ToString();
    }

    private static string Csv(string? value)
    {
        var text = value ?? string.Empty;
        if (text.Length > 0 && "=+-@\t\r".Contains(text[0]))
        {
            text = "'" + text;
        }

        return "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static string Digits(string text) => Animora.Desktop.Localization.PersianNumberFormatter.ToPersianDigits(text);
}
