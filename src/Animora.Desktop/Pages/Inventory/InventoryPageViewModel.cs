using System.Collections.ObjectModel;
using Animora.Contracts.Inventory;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Services;
using Animora.Desktop.Shell;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Inventory;

/// <summary>
/// ViewModel for the Inventory screen (انبار و کالا). Mirrors the reference's
/// page controller (Animora-UI/assets/inventory.js, the list/settings/sub-nav
/// section): the tier-1 sub-nav (کالاها / تنظیمات کالاها) with live counts
/// (<c>renderSubnav</c>), the tier-2 products toolbar (category + stock filter
/// chips and the search field — <c>renderProducts</c>), the product table
/// (<c>renderProductRow</c>) with inline variant sub-rows and its empty state
/// (<c>emptyState</c>), and the Settings sub-view (<c>renderSettings</c>:
/// category management + inventory toggles). Opening the product / new-product
/// / stock-adjust modals is stubbed to the Phase 18 placeholder toast. All data
/// flows through <see cref="IInventoryQueryService"/> (the single backend swap
/// point, R-18) — no sample data or mock branching leaks into this public shape.
/// </summary>
public sealed partial class InventoryPageViewModel : PageViewModelBase
{
    private readonly IInventoryQueryService _service;
    private readonly IToastService _toast;
    private readonly IDialogService _dialogs;

    private readonly List<Product> _products = [];
    private readonly List<CategoryInfo> _categories = [];

    // Reference page state: state.tab, state.search, state.catFilter, state.stockFilter.
    private string _subnav = "products";
    private string _search = string.Empty;
    private string _catFilter = "all";
    private string _stockFilter = "all";

    // Debounced search (~160ms after typing stops), moved out of the code-behind.
    private readonly Debouncer _searchDebounce = new(System.TimeSpan.FromMilliseconds(160));

    public InventoryPageViewModel(IInventoryQueryService service, IToastService toast, IDialogService dialogs)
    {
        _service = service;
        _toast = toast;
        _dialogs = dialogs;

        Title = "انبار و کالا";
        Eyebrow = "مرکز انبار";
        Breadcrumbs.Add("کسب و کار / انبار");

        Tabs.Add(new InventorySubnavViewModel("products", "کالاها", "IconBox"));
        Tabs.Add(new InventorySubnavViewModel("settings", "تنظیمات کالاها", "IconTools"));

        _ = LoadAsync();
    }

    /// <summary>products | settings — the reference's <c>state.tab</c>.</summary>
    [ObservableProperty]
    private string _activeSubnav = "products";

    public bool IsProductsTab => ActiveSubnav == "products";
    public bool IsSettingsTab => ActiveSubnav == "settings";

    /// <summary>The header «کالای جدید» button only shows on the products tab (reference bindHeaderNew).</summary>
    public bool ShowHeaderNew => IsProductsTab;

    public ObservableCollection<InventorySubnavViewModel> Tabs { get; } = [];
    public ObservableCollection<InventoryCategoryChipViewModel> CategoryChips { get; } = [];
    public ObservableCollection<InventoryStockChipViewModel> StockChips { get; } = [];
    public RangeObservableCollection<InventoryProductRowViewModel> FilteredProducts { get; } = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _hasSearchValue;

    [ObservableProperty]
    private bool _hasProducts;

    // ── Settings sub-view ───────────────────────────────────────────────────
    public ObservableCollection<InventoryCategoryRowViewModel> CategoryRows { get; } = [];
    public ObservableCollection<InventorySettingToggleViewModel> SettingToggles { get; } = [];

    [ObservableProperty]
    private string _settingsCountText = string.Empty;

    partial void OnActiveSubnavChanged(string value)
    {
        OnPropertyChanged(nameof(IsProductsTab));
        OnPropertyChanged(nameof(IsSettingsTab));
        OnPropertyChanged(nameof(ShowHeaderNew));
    }

    private async Task LoadAsync()
    {
        IsBusy = true;

        _categories.Clear();
        _categories.AddRange(await _service.GetCategoriesAsync());

        _products.Clear();
        _products.AddRange(await _service.GetProductsAsync());

        IsBusy = false;

        BuildSubnavCounts();
        BuildFilterChips();
        RefreshProducts();
        BuildSettings();
    }

    private void BuildSubnavCounts()
    {
        foreach (var tab in Tabs)
        {
            var count = tab.Key == "products" ? _products.Count : _categories.Count;
            tab.CountText = PersianNumberFormatter.ToPersianDigits(count);
            tab.IsActive = tab.Key == _subnav;
        }
    }

    private void BuildFilterChips()
    {
        CategoryChips.Clear();
        CategoryChips.Add(new InventoryCategoryChipViewModel("all", "همه دسته‌ها", null)
        {
            IsActive = _catFilter == "all",
        });
        foreach (var category in _categories)
        {
            CategoryChips.Add(new InventoryCategoryChipViewModel(category.Id, category.Label, category.Tone)
            {
                IsActive = _catFilter == category.Id,
            });
        }

        if (StockChips.Count == 0)
        {
            StockChips.Add(new InventoryStockChipViewModel("all", "همه وضعیت‌ها"));
            StockChips.Add(new InventoryStockChipViewModel("low", "کم‌موجود"));
            StockChips.Add(new InventoryStockChipViewModel("ok", "در دسترس"));
        }

        foreach (var chip in StockChips)
        {
            chip.IsActive = chip.Key == _stockFilter;
        }
    }

    /// <summary>
    /// Applies the reference's <c>renderProducts</c> filters (category, stock
    /// status, and the name/code/SKU search) and rebuilds the row list.
    /// </summary>
    private void RefreshProducts()
    {
        var query = PersianText.Normalize(_search);

        IEnumerable<Product> items = _products;

        if (_catFilter != "all")
        {
            items = items.Where(p => p.CategoryId == _catFilter);
        }

        if (_stockFilter == "low")
        {
            items = items.Where(p => StockStatus(p) != "ok");
        }
        else if (_stockFilter == "ok")
        {
            items = items.Where(p => StockStatus(p) == "ok");
        }

        if (query.Length > 0)
        {
            items = items.Where(p =>
            {
                if (PersianText.Normalize(p.Name).Contains(query, StringComparison.Ordinal)) return true;
                if (PersianText.Normalize(p.Code).Contains(query, StringComparison.Ordinal)) return true;
                return p.Variants.Any(v => PersianText.Normalize(v.Sku).Contains(query, StringComparison.Ordinal));
            });
        }

        FilteredProducts.ReplaceAll(items.Select(product =>
        {
            var category = _categories.FirstOrDefault(c => c.Id == product.CategoryId) ?? _categories[0];
            var hasVariants = product.Variants.Count > 1;
            return new InventoryProductRowViewModel(product, category, hasVariants);
        }));

        // Preserve any previously-expanded row state across a re-render.
        HasProducts = FilteredProducts.Count > 0;
    }

    /// <summary>Mirrors the reference's <c>stockStatus(p)</c>.</summary>
    private static string StockStatus(Product product)
    {
        var lowCount = product.Variants.Count(v => v.Stock <= v.ReorderPoint);
        if (lowCount == product.Variants.Count) return "out";
        return lowCount > 0 ? "low" : "ok";
    }

    private void BuildSettings()
    {
        CategoryRows.Clear();
        foreach (var category in _categories)
        {
            var count = _products.Count(p => p.CategoryId == category.Id);
            CategoryRows.Add(new InventoryCategoryRowViewModel(
                category.Id, category.Label, category.Tone, category.IconKey, count));
        }

        SettingsCountText =
            $"{PersianNumberFormatter.ToPersianDigits(_categories.Count)} دسته فعال · تنظیمات دسته‌بندی و برچسب کالاها را اینجا مدیریت کنید.";

        if (SettingToggles.Count == 0)
        {
            SettingToggles.Add(new InventorySettingToggleViewModel(
                "هشدار کم‌موجودی", "نمایش نشان هشدار وقتی موجودی به نقطه سفارش رسید.", true));
            SettingToggles.Add(new InventorySettingToggleViewModel(
                "ردیابی تاریخ انقضا", "نمایش ستون انقضا و هشدار نزدیک شدن به تاریخ.", true));
            SettingToggles.Add(new InventorySettingToggleViewModel(
                "کد کالای خودکار", "تولید خودکار کد هنگام ساخت کالای جدید.", false));
        }
    }

    // ── commands ────────────────────────────────────────────────────────────

    /// <summary>
    /// Switches the active sub-nav (tier-1), mirroring the reference's
    /// <c>bindSubnav</c>: resets search/category/stock filters and re-renders.
    /// </summary>
    [RelayCommand]
    private void SetSubnav(string? key)
    {
        if (string.IsNullOrEmpty(key)) return;

        _subnav = key;
        ActiveSubnav = key;

        if (key == "settings")
        {
            _search = string.Empty;
            SearchText = string.Empty;
            HasSearchValue = false;
            _catFilter = "all";
            _stockFilter = "all";
            BuildSettings();
        }

        BuildSubnavCounts();
        BuildFilterChips();
        RefreshProducts();
    }

    [RelayCommand]
    private void SetCategoryFilter(string? id)
    {
        if (string.IsNullOrEmpty(id)) return;
        _catFilter = id;
        BuildFilterChips();
        RefreshProducts();
    }

    [RelayCommand]
    private void SetStockFilter(string? key)
    {
        if (string.IsNullOrEmpty(key)) return;
        _stockFilter = key;
        BuildFilterChips();
        RefreshProducts();
    }

    // The bound search box drives the filter through the ViewModel: update the
    // clear-chip flag immediately, then debounce the (heavier) product refresh.
    partial void OnSearchTextChanged(string value)
    {
        HasSearchValue = value.Trim().Length > 0;
        _searchDebounce.Debounce(() => ApplySearch(SearchText));
    }

    public void ApplySearch(string? text)
    {
        _search = text?.Trim() ?? string.Empty;
        RefreshProducts();
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
        HasSearchValue = false;
        ApplySearch(string.Empty);
    }

    /// <summary>Toggles a product's inline variant sub-rows (reference toggle-expand).</summary>
    [RelayCommand]
    private void ToggleExpand(InventoryProductRowViewModel? row)
    {
        if (row is null) return;
        row.IsExpanded = !row.IsExpanded;
    }

    // ── Phase 18: real product / new-product / stock-adjust modals ──────────

    /// <summary>
    /// Opens the product detail/edit modal (reference <c>openProductModal(pid)</c>),
    /// refreshing the list on save/delete (the reference's <c>render()</c>).
    /// </summary>
    [RelayCommand]
    private void OpenProduct(InventoryProductRowViewModel? row)
    {
        if (row is null) return;
        _dialogs.ShowModal(new ProductModalViewModel(_service, _toast, _dialogs, row.Product.Id, Refresh), 680);
    }

    /// <summary>
    /// Opens the new-product modal (reference <c>openNewProductModal()</c>),
    /// refreshing the list once a product is added.
    /// </summary>
    [RelayCommand]
    private void OpenNewProduct()
    {
        if (!IsProductsTab)
        {
            SetSubnav("products");
        }

        _dialogs.ShowModal(new NewProductModalViewModel(_service, _toast, _dialogs, Refresh), 640);
    }

    /// <summary>
    /// Opens the stock-adjust modal (reference <c>openStockAdjustModal(pid)</c>),
    /// refreshing the list once stock is committed.
    /// </summary>
    [RelayCommand]
    private void OpenStockAdjust(InventoryProductRowViewModel? row)
    {
        if (row is null) return;
        _dialogs.ShowModal(new StockAdjustModalViewModel(_service, _toast, _dialogs, row.Product.Id, Refresh), 480);
    }

    /// <summary>
    /// Opens the product modal on its variants tab (reference edit-variant action
    /// jumps into <c>openProductModal(pid, 'variants')</c>).
    /// </summary>
    [RelayCommand]
    private void EditVariant(InventoryVariantRowViewModel? variant)
    {
        if (variant is null) return;
        var modal = new ProductModalViewModel(_service, _toast, _dialogs, variant.ProductId, Refresh)
        {
            ActiveTab = "variants",
        };
        _dialogs.ShowModal(modal, 680);
    }

    /// <summary>Re-reads the catalog and rebuilds every derived section (reference <c>render()</c>).</summary>
    private void Refresh() => _ = ReloadAsync();

    private async Task ReloadAsync()
    {
        IsBusy = true;
        _categories.Clear();
        _categories.AddRange(await _service.GetCategoriesAsync());
        _products.Clear();
        _products.AddRange(await _service.GetProductsAsync());
        IsBusy = false;

        BuildSubnavCounts();
        BuildFilterChips();
        RefreshProducts();
        BuildSettings();
    }

    // ── settings actions (reference new-cat / save-settings / del-cat) ──────

    [RelayCommand]
    private void AddCategory() => _toast.Show("افزودن دسته — نمونه طراحی");

    [RelayCommand]
    private void SaveSettings() => _toast.Show("تنظیمات ذخیره شد — نمونه طراحی");

    [RelayCommand]
    private void PickTone(InventoryToneOptionViewModel? option)
    {
        if (option is null) return;

        var row = CategoryRows.FirstOrDefault(r => r.ToneOptions.Contains(option));
        row?.ApplyTone(option.Tone);
    }

    [RelayCommand]
    private void DeleteCategory(InventoryCategoryRowViewModel? row)
    {
        if (row is null) return;

        if (_products.Any(p => p.CategoryId == row.Id))
        {
            _toast.Show("این دسته دارای کالاست و حذف نمی‌شود", Tone.Warn);
            return;
        }

        _toast.Show("دسته حذف شد — نمونه طراحی");
    }
}
