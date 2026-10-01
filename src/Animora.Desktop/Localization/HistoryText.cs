using System;
using System.Collections.Generic;
using System.Text.Json;

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

    public static string Detail(string args)
    {
        if (string.IsNullOrWhiteSpace(args)) return string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(args);
            var fields = new List<string>();
            foreach (var property in doc.RootElement.EnumerateObject())
                fields.Add($"{Label(property.Name)}: {property.Value.ToString()}");
            return string.Join("، ", fields);
        }
        catch (JsonException) { return args; }
    }

    private static string Label(string name) => name switch
    {
        "PetId" => "بیمار", "OwnerId" => "مالک", "RoomId" => "اتاق",
        "InvoiceId" => "فاکتور", "VaccineCode" => "واکسن", "Reason" => "علت",
        "category" => "دسته", "from" => "از", "to" => "به", "Currency" => "ارز",
        _ => name,
    };
}
