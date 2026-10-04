using System.Collections.ObjectModel;
using Animora.Contracts.Inventory;
using Animora.Desktop.Localization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Animora.Desktop.Pages.Inventory;

/// <summary>
/// One pill of the page-chrome sub-nav (tier-1), mirroring the reference's
/// <c>.inv-subnav button</c> rendered by <c>renderSubnav</c> (کالاها /
/// تنظیمات کالاها): a Persian label, its icon key and a live count pill
/// (rendered as Persian digits).
/// </summary>
public sealed partial class InventorySubnavViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private string _countText = string.Empty;

    public InventorySubnavViewModel(string key, string label, string iconKey)
    {
        Key = key;
        Label = label;
        IconKey = iconKey;
    }

    /// <summary>products | settings — the reference's data-tab keys.</summary>
    public string Key { get; }

    public string Label { get; }

    public string IconKey { get; }
}

/// <summary>
/// A single category filter chip in the tier-2 toolbar, mirroring the
/// reference's <c>.pb-chip</c> rendered by <c>renderProducts</c> — «همه دسته‌ها»
/// plus one per category (with its tone dot).
/// </summary>
public sealed partial class InventoryCategoryChipViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isActive;

    public InventoryCategoryChipViewModel(string id, string label, string? tone)
    {
        Id = id;
        Label = label;
        Tone = tone;
    }

    /// <summary>"all" or a category id — the reference's data-cat value.</summary>
    public string Id { get; }

    public string Label { get; }

    /// <summary>Null for the «همه دسته‌ها» chip (no coloured dot).</summary>
    public string? Tone { get; }

    public bool HasDot => Tone is not null;

    public IBrush DotBrush => InventoryPresentation.ChipDot(Tone ?? "muted");
}

/// <summary>
/// A single stock-status filter chip, mirroring the reference's
/// <c>.pb-chip</c> group rendered by <c>renderProducts</c>
/// («همه وضعیت‌ها» / «کم‌موجود» / «در دسترس»).
/// </summary>
public sealed partial class InventoryStockChipViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isActive;

    public InventoryStockChipViewModel(string key, string label)
    {
        Key = key;
        Label = label;
    }

    /// <summary>all | low | ok — the reference's data-stock value.</summary>
    public string Key { get; }

    public string Label { get; }
}

/// <summary>
/// One inline variant sub-row of an expanded product, mirroring the reference's
/// <c>.subrow</c> rendered by <c>renderProductRow</c>: its label, SKU, stock
/// (with the «کم‌موجود» badge when low), cost, sale price, expiry and an edit
/// action that jumps into the product modal's variants tab (Phase 18).
/// </summary>
public sealed class InventoryVariantRowViewModel
{
    public InventoryVariantRowViewModel(ProductVariant variant, string productId)
    {
        Id = variant.Id;
        ProductId = productId;
        Label = variant.Label;
        Sku = variant.Sku;
        IsLow = StockStatus.IsLow(variant);
        StockText = $"{InventoryPresentation.Money(variant.Stock)} عدد";
        CostText = $"{InventoryPresentation.Money(variant.Cost)} ت";
        PriceText = $"{InventoryPresentation.Money(variant.Price)} ت";
        ExpiryText = variant.Expiry ?? "—";
    }

    public string Id { get; }

    /// <summary>The owning product's id (used by the edit-variant action to open its modal).</summary>
    public string ProductId { get; }

    public string Label { get; }

    public string Sku { get; }

    public bool IsLow { get; }

    public string StockText { get; }

    public string CostText { get; }

    public string PriceText { get; }

    public string ExpiryText { get; }
}

/// <summary>
/// One product row of the inventory table, mirroring the reference's
/// <c>renderProductRow</c> (Animora-UI/assets/inventory.js): the expand toggle,
/// thumbnail + name/brand, code chip, category tag, variant count, total stock
/// (with progress bar), price range and status pill — plus the inline variant
/// sub-rows when the row is expanded. Every derived value (stock total, price
/// range, status, stock percentage) is computed from the product's variants,
/// exactly as the reference derives them.
/// </summary>
public sealed partial class InventoryProductRowViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isExpanded;

    public InventoryProductRowViewModel(Product product, CategoryInfo category, bool hasVariants)
    {
        Product = product;
        Name = product.Name;
        Brand = product.Brand;
        Code = product.Code;

        var (bg, fg) = InventoryPresentation.CategoryTag(category.Tone);
        CategoryBackground = bg;
        CategoryForeground = fg;
        CategoryLabel = category.Label;

        HasVariants = hasVariants;
        VariantCountText = PersianNumberFormatter.ToPersianDigits(product.Variants.Count);

        // Stock status comes from the one shared rule (StockStatus, P2-16) so the
        // row badge/bar and the per-variant «کم‌موجود» flag can never drift.
        var status = StockStatus.Key(StockStatus.Of(product.Variants));

        var stock = product.Variants.Sum(v => v.Stock);
        StockText = $"{InventoryPresentation.Money(stock)} عدد";

        var peak = Math.Max(stock, product.Variants.Sum(v => v.ReorderPoint * 3));
        var percent = Math.Clamp((int)Math.Round(stock / (double)(peak == 0 ? 1 : peak) * 100), 0, 100);
        StockNumberForeground = InventoryPresentation.PriceForeground(status);
        StockBar = InventoryPresentation.StockBarBrush(status, percent);

        var prices = product.Variants.Select(v => v.Price).ToList();
        var min = prices.Min();
        var max = prices.Max();
        PriceMin = InventoryPresentation.Money(min);
        PriceMax = InventoryPresentation.Money(max);
        HasPriceRange = min != max;
        PriceForeground = InventoryPresentation.PriceForeground(status);

        (StatusLabel, StatusForeground, StatusDot, StatusRing) = status switch
        {
            "ok" => ("در دسترس",
                InventoryPresentation.StatusForeground("ok"),
                InventoryPresentation.StatusDot("ok"),
                InventoryPresentation.Fill("#262F8F6A")),
            "low" => ("هشدار",
                InventoryPresentation.StatusForeground("low"),
                InventoryPresentation.StatusDot("warn"),
                InventoryPresentation.Fill("#26C98A2B")),
            _ => ("اتمام",
                InventoryPresentation.StatusForeground("out"),
                InventoryPresentation.StatusDot("low"),
                InventoryPresentation.Fill("#26D0403A")),
        };

        Variants = new ObservableCollection<InventoryVariantRowViewModel>(
            product.Variants.Select(v => new InventoryVariantRowViewModel(v, product.Id)));
    }

    public Product Product { get; }

    public string Name { get; }

    public string Brand { get; }

    public string Code { get; }

    public IBrush CategoryBackground { get; }

    public IBrush CategoryForeground { get; }

    public string CategoryLabel { get; }

    public bool HasVariants { get; }

    public string VariantCountText { get; }

    public string StockText { get; }

    public IBrush StockNumberForeground { get; }

    public IBrush StockBar { get; }

    public string PriceMin { get; }

    public string PriceMax { get; }

    public bool HasPriceRange { get; }

    public IBrush PriceForeground { get; }

    public string StatusLabel { get; }

    public IBrush StatusForeground { get; }

    public IBrush StatusDot { get; }

    /// <summary>The translucent 3px halo behind the status dot (<c>.status-dot::before</c> box-shadow).</summary>
    public IBrush StatusRing { get; }

    /// <summary>
    /// The row's fill: <c>#f7fbfa</c> while expanded (<c>.prow.expanded</c>),
    /// otherwise the panel white.
    /// </summary>
    public IBrush RowBackground => IsExpanded
        ? InventoryPresentation.Fill("#f7fbfa")
        : InventoryPresentation.Fill("#ffffff");

    public ObservableCollection<InventoryVariantRowViewModel> Variants { get; }

    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(RowBackground));
}
