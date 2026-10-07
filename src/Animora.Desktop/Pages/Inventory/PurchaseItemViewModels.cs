using Animora.Contracts.Inventory;
using Animora.Contracts.Purchasing;
using Animora.Desktop.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Animora.Desktop.Pages.Inventory;

/// <summary>One recorded supplier purchase invoice of the «خرید و تأمین» table (already formatted by the service).</summary>
public sealed class PurchaseRowViewModel(PurchaseListItem item)
{
    public string Number { get; } = PersianNumberFormatter.ToPersianDigits(item.Number);

    public string SupplierName { get; } = item.SupplierName;

    public string DateText { get; } = PersianNumberFormatter.ToPersianDigits(item.DateJalali);

    public string LineCountText { get; } = PersianNumberFormatter.ToPersianDigits(item.LineCount);

    public string TotalText { get; } = InventoryPresentation.Money(item.Total);

    public string ReferenceText { get; } =
        string.IsNullOrWhiteSpace(item.InvoiceReference) ? "—" : PersianNumberFormatter.ToPersianDigits(item.InvoiceReference);

    public string StateLabel { get; } = item.StateLabel;
}

/// <summary>One pickable (product, variant) of the purchase-invoice item picker — «اتوفیل» of cost + stock.</summary>
public sealed class PurchaseProductOption(Product product, ProductVariant variant)
{
    public Product Product { get; } = product;

    public ProductVariant Variant { get; } = variant;

    public string Title => Product.Variants.Count > 1 ? $"{Product.Name} · {Variant.Label}" : Product.Name;

    public string Sub =>
        $"{Product.Code} · موجودی {PersianNumberFormatter.ToPersianDigits(Variant.Stock)}"
        + (Variant.Cost > 0 ? $" · آخرین خرید {InventoryPresentation.Money(Variant.Cost)} تومان" : string.Empty);
}

/// <summary>
/// One line of the purchase invoice: either an existing product variant (cost auto-filled from its current purchase
/// price) or a brand-new product that is created only when the invoice is recorded (so cancelling leaves no orphan).
/// </summary>
public sealed partial class PurchaseLineViewModel : ObservableObject
{
    private readonly Action _changed;

    public PurchaseLineViewModel(
        string productId, string variantId, string title, string sub, long unitCost, int quantity, Action changed)
    {
        ProductId = productId;
        VariantId = variantId;
        Title = title;
        Sub = sub;
        _changed = changed;
        _qtyText = PersianNumberFormatter.ToPersianDigits(Math.Max(1, quantity));
        _costText = PersianNumberFormatter.ToPersianDigits(Math.Max(0, unitCost));
    }

    /// <summary>Empty until the pending new product has been created.</summary>
    public string ProductId { get; set; }

    public string VariantId { get; set; }

    public string Title { get; }

    public string Sub { get; }

    /// <summary>True for a brand-new good — created through the inventory service when the invoice is recorded.</summary>
    public bool IsNew { get; init; }

    public string NewCode { get; init; } = string.Empty;

    public string NewCategoryId { get; init; } = string.Empty;

    public long NewSalePrice { get; init; }

    [ObservableProperty]
    private string _qtyText;

    [ObservableProperty]
    private string _costText;

    public int Quantity => int.TryParse(Digits(QtyText), out var v) ? Math.Max(0, v) : 0;

    public long UnitCost => long.TryParse(Digits(CostText), out var v) ? Math.Max(0, v) : 0;

    public long LineTotal => (long)Quantity * UnitCost;

    public string LineTotalText => InventoryPresentation.Money(LineTotal);

    partial void OnQtyTextChanged(string value) => Touch();

    partial void OnCostTextChanged(string value) => Touch();

    /// <summary>Adds one more unit when the same variant is picked again.</summary>
    public void Increment() => QtyText = PersianNumberFormatter.ToPersianDigits(Quantity + 1);

    private void Touch()
    {
        OnPropertyChanged(nameof(LineTotalText));
        _changed();
    }

    private static string Digits(string? text) =>
        new(PersianNumberFormatter.ToAsciiDigits(text ?? string.Empty).Where(char.IsDigit).ToArray());
}
