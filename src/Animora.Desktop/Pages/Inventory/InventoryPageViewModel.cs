using System.Collections.ObjectModel;
using Animora.Contracts.Diagnostics;
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
    private readonly IDataChangeNotifier _changes;
    private readonly IDialogService _dialogs;

    // Server-side paging: _loaded holds only the pages fetched for the active category / stock / search filters.
    private const int ListPageSize = 50;
    private const int ListHardMax = 200;
    private readonly List<Product> _loaded = [];
    private int _listTotal;
    private int _catalogTotal;
    private int _listGeneration;
    private readonly List<CategoryInfo> _categories = [];
    private readonly IStoreReadinessGate _readiness;

    // Reference page state: state.tab, state.search, state.catFilter, state.stockFilter.
    private string _subnav = "products";
    private string _search = string.Empty;
    private string _catFilter = "all";
    private string _stockFilter = "all";

    // Debounced search (~160ms after typing stops), moved out of the code-behind.
    private readonly Debouncer _searchDebounce = new(System.TimeSpan.FromMilliseconds(160));

    public InventoryPageViewModel(IInventoryQueryService service, IStoreReadinessGate readiness,
        IToastService toast, IDialogService dialogs,
        IDataChangeNotifier changes)
    {
        _service = service;
        _changes = changes;
        _readiness = readiness;
        _toast = toast;
        _dialogs = dialogs;

        Title = "انبار و کالا";
        Eyebrow = "مرکز انبار";
        Breadcrumbs.Add("کسب و کار / انبار");

        Tabs.Add(new InventorySubnavViewModel("products", "کالاها", "IconBox"));
        Tabs.Add(new InventorySubnavViewModel("settings", "تنظیمات کالاها", "IconTools"));

        UseLoadStatus(ReloadPageAsync);
        // Stock moves with sales, payments and purchases.
        // A hidden page is never reloaded behind the user's back (the base class gates on visibility); the
        // entry hook re-reads on the next visit, and a visible refresh re-reads only the rows already shown.
        RefreshOnDataChange(changes, () => _ = ReloadPageAsync(), DataDomain.Sales, DataDomain.Cashier, DataDomain.Purchasing);
    }

    /// <summary>products | settings — the reference's <c>state.tab</c>.</summary>
    [ObservableProperty]
    private string _activeSubnav = "products";

    public bool IsProductsTab => ActiveSubnav == "products";
    public bool IsSettingsTab => ActiveSubnav == "settings";

    /// <summary>The header «کالای جدید» button only shows on the products tab (reference bindHeaderNew).</summary>
    public bool ShowHeaderNew => IsProductsTab;

    public ObservableCollection<InventorySubnavViewModel> Tabs { get; } = [];
    public RangeObservableCollection<InventoryCategoryChipViewModel> CategoryChips { get; } = [];
    public ObservableCollection<InventoryStockChipViewModel> StockChips { get; } = [];
    public RangeObservableCollection<InventoryProductRowViewModel> FilteredProducts { get; } = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _hasSearchValue;

    [ObservableProperty]
    private bool _hasProducts;

    // ── Settings sub-view ───────────────────────────────────────────────────
    public RangeObservableCollection<InventoryCategoryRowViewModel> CategoryRows { get; } = [];
    public ObservableCollection<InventorySettingToggleViewModel> SettingToggles { get; } = [];

    [ObservableProperty]
    private string _settingsCountText = string.Empty;

    partial void OnActiveSubnavChanged(string value)
    {
        OnPropertyChanged(nameof(IsProductsTab));
        OnPropertyChanged(nameof(IsSettingsTab));
        OnPropertyChanged(nameof(ShowHeaderNew));
    }


    // Singleton page: reload on every visit (not only at construction) so data changed elsewhere is never stale.
    // Failures surface in the shell's load-state block, which also offers the manual refresh.
    public override void OnNavigatedTo()
    {
        if (!IsLoadInFlight) _ = ReloadPageAsync();
    }

    private Task ReloadPageAsync() => TrackLoadAsync(LoadAsync);

    private async Task LoadAsync()
    {
        await _readiness.WaitAsync();
        IsBusy = true;
        try
        {
            _categories.Clear();
            _categories.AddRange(await _service.GetCategoriesAsync());

            await FetchListAsync(append: false);
        }
        finally
        {
            IsBusy = false;
        }

        RenderAll();
    }

    /// <summary>Rebuilds every section derived from the loaded categories + current page window.</summary>
    private void RenderAll()
    {
        BuildSubnavCounts();
        BuildFilterChips();
        RenderProducts();
        BuildSettings();
    }

    /// <summary>
    /// Fetches the active filters' rows from the query: the first page (or as many rows as are already shown,
    /// so a refresh keeps the user's place) or, with <paramref name="append"/>, the next page. Filtering,
    /// ordering and paging all run in SQL; a generation token drops a slower stale response.
    /// </summary>
    private async Task FetchListAsync(bool append)
    {
        var token = ++_listGeneration;
        var search = _search.Length == 0 ? null : _search;
        var category = _catFilter == "all" ? null : _catFilter;
        var stock = _stockFilter switch
        {
            "low" => ProductStockFilter.Low,
            "ok" => ProductStockFilter.Ok,
            _ => ProductStockFilter.All,
        };

        ProductPage page;
        if (append)
        {
            var next = _loaded.Count / ListPageSize + 1;
            page = await _service.GetProductsAsync(new ProductListQuery(search, category, stock, next, ListPageSize));
        }
        else
        {
            var size = Math.Clamp(_loaded.Count, ListPageSize, ListHardMax);
            page = await _service.GetProductsAsync(new ProductListQuery(search, category, stock, 1, size));
        }

        if (token != _listGeneration)
        {
            return; // a newer query superseded this one - drop the stale result
        }

        if (!append) _loaded.Clear();
        _loaded.AddRange(page.Items);
        _listTotal = page.TotalCount;
        _catalogTotal = page.CatalogTotal;
    }

    /// <summary>Re-queries after a filter / search change and re-renders the list (not the settings tab).</summary>
    private async Task ReloadListAsync(bool append)
    {
        try
        {
            await FetchListAsync(append);
            BuildSubnavCounts();
            RenderProducts();
        }
        catch (InvalidOperationException ex)
        {
            _toast.Show(InventoryPresentation.MutationFailureMessage(ex, "بارگذاری فهرست کالاها ناموفق بود؛ دوباره تلاش کنید"), Tone.Danger);
        }
    }

    /// <summary>Whether more rows exist beyond the pages already loaded for the active filters.</summary>
    public bool CanLoadMore => _loaded.Count < _listTotal;

    [RelayCommand]
    private Task LoadMore() => ReloadListAsync(append: true);

    private void BuildSubnavCounts()
    {
        foreach (var tab in Tabs)
        {
            var count = tab.Key == "products" ? _catalogTotal : _categories.Count;
            tab.CountText = PersianNumberFormatter.ToPersianDigits(count);
            tab.IsActive = tab.Key == _subnav;
        }
    }

    private void BuildFilterChips()
    {
        CategoryChips.ReplaceAll(new[]
            {
                new InventoryCategoryChipViewModel("all", "همه دسته‌ها", null)
                {
                    IsActive = _catFilter == "all",
                },
            }
            .Concat(_categories.Select(category => new InventoryCategoryChipViewModel(category.Id, category.Label, category.Tone)
            {
                IsActive = _catFilter == category.Id,
            })));

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
    /// Rebuilds the row list from the loaded page window (reference <c>renderProducts</c>). Category, stock
    /// status and search were already applied by the query, so rows only change when the window changes.
    /// </summary>
    private void RenderProducts()
    {
        FilteredProducts.ReplaceAll(_loaded.Select(product =>
        {
            var category = _categories.FirstOrDefault(c => c.Id == product.CategoryId)
                           ?? (_categories.Count > 0 ? _categories[0] : new CategoryInfo(product.CategoryId, string.Empty, "teal", string.Empty));
            var hasVariants = product.Variants.Count > 1;
            return new InventoryProductRowViewModel(product, category, hasVariants);
        }));

        HasProducts = FilteredProducts.Count > 0;
        OnPropertyChanged(nameof(CanLoadMore));
    }

    private void BuildSettings()
    {
        CategoryRows.ReplaceAll(_categories.Select(category =>
        {
            return new InventoryCategoryRowViewModel(
                category.Id, category.Label, category.Tone, category.IconKey, category.ProductCount);
        }));

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
        if (key == "settings") return;

        _ = ReloadListAsync(append: false);
    }

    [RelayCommand]
    private void SetCategoryFilter(string? id)
    {
        if (string.IsNullOrEmpty(id)) return;
        _catFilter = id;
        BuildFilterChips();
        _ = ReloadListAsync(append: false);
    }

    [RelayCommand]
    private void SetStockFilter(string? key)
    {
        if (string.IsNullOrEmpty(key)) return;
        _stockFilter = key;
        BuildFilterChips();
        _ = ReloadListAsync(append: false);
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
        var normalized = text?.Trim() ?? string.Empty;
        if (string.Equals(normalized, _search, StringComparison.Ordinal)) return;

        _search = normalized;
        _ = ReloadListAsync(append: false);
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
    private void Refresh()
    {
        _changes.Notify(DataDomain.Inventory, DataDomain.Dashboard);
        _ = ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        IsBusy = true;
        try
        {
            _categories.Clear();
            _categories.AddRange(await _service.GetCategoriesAsync());
            await FetchListAsync(append: false);
        }
        finally
        {
            IsBusy = false;
        }

        RenderAll();
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

        if (_categories.Any(c => c.Id == row.Id && c.ProductCount > 0))
        {
            _toast.Show("این دسته دارای کالاست و حذف نمی‌شود", Tone.Warn);
            return;
        }

        _toast.Show("دسته حذف شد — نمونه طراحی");
    }
}
