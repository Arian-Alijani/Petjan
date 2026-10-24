using System.Collections.ObjectModel;
using Animora.Contracts.Inventory;
using Animora.Desktop.Localization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Inventory;

/// <summary>
/// One variant editor card of the product modal's Variants tab, mirroring the
/// reference's <c>.var-card</c> rendered by <c>renderVariantCard</c> (inventory.js):
/// the editable label, its SKU chip, a delete action and the field grid
/// (read-only stock, reorder, cost, sale price, expiry). On-hand stock is a
/// projection of stock movements, so it is displayed only and changes through the «تغییر موجودی» modal.
/// </summary>
public sealed partial class InventoryVariantCardViewModel : ObservableObject
{
    public InventoryVariantCardViewModel(ProductVariant variant)
    {
        Id = variant.Id;
        Sku = variant.Sku;
        _label = variant.Label;
        _stockText = PersianNumberFormatter.ToPersianDigits(variant.Stock);
        _reorderText = PersianNumberFormatter.ToPersianDigits(variant.ReorderPoint);
        _costText = PersianNumberFormatter.FormatGrouped(variant.Cost);
        _priceText = PersianNumberFormatter.FormatGrouped(variant.Price);
        _expiryText = variant.Expiry ?? string.Empty;
        _barcode = variant.Barcode ?? string.Empty;
        _isLow = StockStatus.IsLow(variant);
    }

    public string Id { get; }
    public string Sku { get; }

    [ObservableProperty]
    private string _label;

    [ObservableProperty]
    private string _stockText;

    [ObservableProperty]
    private string _reorderText;

    [ObservableProperty]
    private string _costText;

    [ObservableProperty]
    private string _priceText;

    [ObservableProperty]
    private string _expiryText;

    /// <summary>Scanner barcode of this variant (empty = none).</summary>
    [ObservableProperty]
    private string _barcode;

    [ObservableProperty]
    private bool _isLow;

    /// <summary>The reference's <c>.var-card.low</c> — a red-tinted fill.</summary>
    public IBrush CardBackground => IsLow
        ? InventoryPresentation.LowStockRowBackground()
        : InventoryPresentation.Fill("#ffffff");

    public IBrush CardBorder => IsLow
        ? InventoryPresentation.Fill("#59d0403a")
        : InventoryPresentation.Fill("#e5eaee");

    partial void OnIsLowChanged(bool value) => OnPropertyChanged(nameof(CardBackground));

    /// <summary>Recomputes the low-stock flag after the reorder point is edited.</summary>
    public void RefreshLow() => IsLow = ReadStock() <= ReadReorder();

    public int ReadStock() =>
        int.TryParse(PersianNumberFormatter.ToAsciiDigits(StockText), out var v) ? v : 0;

    public int ReadReorder() =>
        int.TryParse(PersianNumberFormatter.ToAsciiDigits(ReorderText), out var v) ? v : 0;

    public long ReadCost() =>
        long.TryParse(PersianNumberFormatter.ToAsciiDigits(CostText), out var v) ? v : 0;

    public long ReadPrice() =>
        long.TryParse(PersianNumberFormatter.ToAsciiDigits(PriceText), out var v) ? v : 0;

    /// <summary>Projects the edited card back onto a domain variant (reference «ذخیره تغییرات»).</summary>
    public ProductVariant ToVariant() => new(
        Id,
        Label,
        Sku,
        ReadStock(),
        ReadReorder(),
        ReadCost(),
        ReadPrice(),
        string.IsNullOrWhiteSpace(ExpiryText) ? null : ExpiryText.Trim(),
        Barcode: string.IsNullOrWhiteSpace(Barcode) ? null : Barcode.Trim());
}

/// <summary>
/// One option chip of the attribute builder, mirroring the reference's
/// <c>.opt-chip</c> rendered by <c>renderVariantsTab</c> (a label plus a remove
/// action).
/// </summary>
public sealed class InventoryOptionChipViewModel
{
    public InventoryOptionChipViewModel(string label) => Label = label;

    public string Label { get; }
}

/// <summary>
/// One stock-movement row of the History tab timeline, mirroring the reference's
/// <c>.hist-item</c> rendered by <c>renderHistoryTab</c> (inventory.js): a toned
/// kind icon, the source title, the signed quantity, the variant chip and the
/// date / author meta line plus the optional note box.
/// </summary>
public sealed class InventoryHistoryItemViewModel
{
    public InventoryHistoryItemViewModel(StockMovement movement, string variantLabel)
    {
        var (iconKey, sign, kindClass) = movement.Kind switch
        {
            "in" => ("IconArrowDownLeft", "+", "in"),
            "out" => ("IconArrowUpRight", "−", "out"),
            _ => ("IconRefresh", string.Empty, "adjust"),
        };

        Kind = kindClass;
        IconKey = iconKey;
        Title = movement.Source;
        By = movement.By;
        HasNote = !string.IsNullOrWhiteSpace(movement.Note);
        Note = movement.Note ?? string.Empty;

        var qtyText = movement.Kind == "adjust"
            ? (movement.Qty >= 0 ? "+" : "−") + InventoryPresentation.Money(Math.Abs(movement.Qty))
            : sign + InventoryPresentation.Money(movement.Qty);
        QuantityText = qtyText;

        VariantLabel = variantLabel;
        HasVariant = variantLabel.Length > 0;

        MetaText = movement.Time is null or "—"
            ? movement.Date
            : $"{movement.Date} · {movement.Time}";

        TileBackground = InventoryPresentation.HistoryTileBackground(kindClass);
        TileForeground = InventoryPresentation.HistoryTileForeground(kindClass);
        QuantityForeground = InventoryPresentation.HistoryQuantityForeground(kindClass);
    }

    public string Kind { get; }
    public string IconKey { get; }
    public string Title { get; }
    public string QuantityText { get; }
    public string By { get; }
    public string VariantLabel { get; }
    public bool HasVariant { get; }
    public string MetaText { get; }
    public string Note { get; }
    public bool HasNote { get; }
    public IBrush TileBackground { get; }
    public IBrush TileForeground { get; }
    public IBrush QuantityForeground { get; }
}

/// <summary>
/// One «کالای جدید» modal type tile, mirroring the reference's
/// <c>.type-opt</c> (تک‌واحد / چند‌تنوعی) with its icon, title, sub-line and
/// check tick.
/// </summary>
public sealed partial class InventoryProductTypeOptionViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isActive;

    public InventoryProductTypeOptionViewModel(string key, string iconKey, string title, string sub)
    {
        Key = key;
        IconKey = iconKey;
        Title = title;
        Sub = sub;
    }

    /// <summary>no | yes — the reference's data-choice.</summary>
    public string Key { get; }
    public string IconKey { get; }
    public string Title { get; }
    public string Sub { get; }
}

/// <summary>One stock-adjust modal row, mirroring the reference's <c>.adjust-row</c>.</summary>
public sealed partial class InventoryAdjustRowViewModel : ObservableObject
{
    public InventoryAdjustRowViewModel(ProductVariant variant)
    {
        Id = variant.Id;
        Label = variant.Label;
        Sku = variant.Sku;
        ReorderPoint = variant.ReorderPoint;
        SubText = $"{variant.Sku} · نقطه سفارش {PersianNumberFormatter.ToPersianDigits(variant.ReorderPoint)}";
        OriginalStock = variant.Stock;
        _stockText = PersianNumberFormatter.ToPersianDigits(variant.Stock);
        _isLow = StockStatus.IsLow(variant);
    }

    public string Id { get; }
    public string Label { get; }
    public string Sku { get; }
    public int ReorderPoint { get; }
    public string SubText { get; }
    public int OriginalStock { get; }

    [ObservableProperty]
    private string _stockText;

    [ObservableProperty]
    private bool _isLow;

    public IBrush RowBackground => IsLow
        ? InventoryPresentation.LowStockRowBackground()
        : InventoryPresentation.Fill("#ffffff");

    public IBrush RowBorder => IsLow
        ? InventoryPresentation.Fill("#59d0403a")
        : InventoryPresentation.Fill("#e5eaee");

    partial void OnIsLowChanged(bool value) => OnPropertyChanged(nameof(RowBackground));

    [RelayCommand]
    private void Decrement()
    {
        var stock = Math.Max(0, ReadStock() - 1);
        StockText = PersianNumberFormatter.ToPersianDigits(stock);
        IsLow = StockStatus.IsLow(stock, ReorderPoint);
    }

    [RelayCommand]
    private void Increment()
    {
        var stock = ReadStock() + 1;
        StockText = PersianNumberFormatter.ToPersianDigits(stock);
        IsLow = StockStatus.IsLow(stock, ReorderPoint);
    }

    public int ReadStock() =>
        int.TryParse(PersianNumberFormatter.ToAsciiDigits(StockText), out var v) ? v : 0;
}
