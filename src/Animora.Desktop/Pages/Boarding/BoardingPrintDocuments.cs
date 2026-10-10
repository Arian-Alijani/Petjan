using Animora.Contracts.Boarding;
using Animora.Desktop.Services;

namespace Animora.Desktop.Pages.Boarding;

/// <summary>Print-ready bill of a boarding stay (stay cost + consumed items) — pure formatting (X13).</summary>
public static class BoardingPrintDocuments
{
    public static string StayBill(StayDetail detail, string fontCss)
    {
        var stay = detail.Stay;
        var nights = Math.Max(1, stay.CheckOut.DayNumber - stay.CheckIn.DayNumber);
        var stayCost = (long)nights * stay.DailyRate;
        var itemsCost = detail.Items.Sum(it => (long)it.Quantity * it.Price);

        var meta = new List<PrintMeta>
        {
            new("حیوان", stay.PetName),
            new("سرپرست", stay.OwnerName),
            new("تلفن", string.IsNullOrWhiteSpace(stay.OwnerPhone) ? "—" : stay.OwnerPhone),
            new("اتاق", Digits(stay.Room)),
            new("ورود", BoardingPresentation.Long(stay.CheckIn)),
            new("خروج", BoardingPresentation.Long(stay.CheckOut)),
            new("نحوهٔ پرداخت", BoardingPresentation.PayLabel(stay.PaymentMode)),
        };

        var rows = detail.Items.Select((it, i) => (IReadOnlyList<string>)new[]
        {
            BoardingPresentation.Fa(i + 1),
            it.Title,
            it.Kind == BoardingItemKind.Service ? "خدمت" : "کالا",
            BoardingPresentation.Long(it.At),
            BoardingPresentation.Fa(it.Quantity),
            BoardingPresentation.Money(it.Price),
            BoardingPresentation.Money((long)it.Quantity * it.Price),
        }).ToList();

        return PrintDocumentBuilder.Build(
            "صورتحساب پانسیون",
            $"{stay.PetName} · {stay.OwnerName}",
            meta,
            ["#", "شرح", "نوع", "تاریخ", "تعداد", "قیمت واحد", "مبلغ"],
            rows,
            [
                new PrintTotal(
                    $"اقامت ({BoardingPresentation.Fa(nights)} شب × {BoardingPresentation.Money(stay.DailyRate)})",
                    BoardingPresentation.Money(stayCost)),
                new PrintTotal("خدمات و کالا", BoardingPresentation.Money(itemsCost)),
                new PrintTotal("جمع کل", BoardingPresentation.Money(stayCost + itemsCost), true),
            ],
            fontCss,
            string.IsNullOrWhiteSpace(stay.Note) ? null : "یادداشت: " + stay.Note);
    }

    private static string Digits(string text) => Animora.Desktop.Localization.PersianNumberFormatter.ToPersianDigits(text);
}
