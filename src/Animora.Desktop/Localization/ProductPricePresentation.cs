namespace Animora.Desktop.Localization;

/// <summary>One consistent display rule for catalog prices. Zero means not priced, never a free sale.</summary>
public static class ProductPricePresentation
{
    public const string ContactForPrice = "برای دریافت قیمت تماس بگیرید";
    public static string Format(long price, bool includeCurrency = false) => price <= 0
        ? ContactForPrice
        : PersianNumberFormatter.FormatGrouped(price) + (includeCurrency ? " تومان" : string.Empty);
}
