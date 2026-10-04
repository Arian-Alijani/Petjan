using System;
using System.Collections.Generic;

namespace Animora.Desktop.Localization;

/// <summary>Persian presentation of stable, language-independent activity keys.</summary>
public static class HistoryText
{
    private static readonly IReadOnlyDictionary<string, string> Titles = new Dictionary<string, string>
    {
        ["history.pet.registered"] = "بیمار جدید ثبت شد",
        ["history.visit.completed"] = "ویزیت تکمیل شد",
        ["history.vaccine.administered"] = "واکسیناسیون انجام شد",
        ["history.appointment.booked"] = "نوبت ثبت شد",
        ["history.appointment.cancelled"] = "نوبت لغو شد",
        ["history.stay.activated"] = "پذیرش پانسیون انجام شد",
        ["history.stay.checkedout"] = "ترخیص از پانسیون انجام شد",
        ["history.goods.received"] = "کالا دریافت شد",
        ["history.stock.adjusted"] = "موجودی انبار تغییر کرد",
        ["history.invoice.posted"] = "فاکتور ثبت شد",
        ["history.payment.registered"] = "پرداخت ثبت شد",
        ["history.invoice.refunded"] = "برگشت فاکتور ثبت شد",
        ["history.cheque.statuschanged"] = "وضعیت چک تغییر کرد",
        ["history.expense.recorded"] = "هزینه ثبت شد",
    };

    public static string Title(string key) => Titles.TryGetValue(key, out var value) ? value : key;

    /// <summary>Translates a key; false for null/unknown keys so callers can keep a safe fallback.</summary>
    public static bool TryTitle(string? key, out string title)
    {
        title = string.Empty;
        return key is not null && Titles.TryGetValue(key, out title!);
    }

    /// <summary>
    /// Renders the resolved detail written at feed-write time (<c>Key=Value</c> lines of display names) as
    /// «برچسب: مقدار، …». Pure string splitting — no JSON parsing per rendered row.
    /// </summary>
    public static string Detail(string resolved)
    {
        if (string.IsNullOrWhiteSpace(resolved)) return string.Empty;

        var builder = new System.Text.StringBuilder(resolved.Length + 16);
        foreach (var line in resolved.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;

            if (builder.Length > 0) builder.Append("، ");
            builder.Append(Label(line[..eq])).Append(": ").Append(line, eq + 1, line.Length - eq - 1);
        }

        return builder.ToString();
    }

    private static string Label(string key) => key switch
    {
        "Pet" => "بیمار", "Owner" => "مالک", "Room" => "اتاق", "Invoice" => "فاکتور",
        "Product" => "کالا", "Supplier" => "تأمین‌کننده", "Vaccine" => "واکسن", "Reason" => "علت",
        "Category" => "دسته", "From" => "از", "To" => "به", "Count" => "تعداد",
        "Quantity" => "مقدار", "Method" => "روش",
        _ => key,
    };
}
