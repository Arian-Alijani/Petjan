using System.Collections.ObjectModel;
using Animora.Contracts.Diagnostics;
using Animora.Contracts.Inventory;
using Animora.Contracts.Purchasing;
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
    private readonly IPurchasingService _purchasing;
    private readonly IToastService _toast;
    private readonly IDataChangeNotifier _changes;
    private readonly IDialogService _dialogs;

    // Server-side paging: _loaded holds exactly ONE page for the active category / stock / search filters
    // (previous/next replaces the window — nothing is appended or kept across pages, same as the Patients page).
    // The server hard-caps a page at 200 rows (QueryPaging.HardMaxPageSize); "همه" maps to that ceiling.
    private const int ServerMaxPageSize = 200;
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

    public InventoryPageViewModel(IInventoryQueryService service, IPurchasingService purchasing,
        IStoreReadinessGate readiness, IToastService toast, IDialogService dialogs,
        IDataChangeNotifier changes)
    {
        _service = service;
        _purchasing = purchasing;
        _changes = changes;
        _readiness = readiness;
        _toast = toast;
        _dialogs = dialogs;

        Title = "انبار و کالا";
        Eyebrow = "مرکز انبار";
        Breadcrumbs.Add("کسب و کار / انبار");

        Tabs.Add(new InventorySubnavViewModel("products", "کالاها", "IconBox"));
        Tabs.Add(new InventorySubnavViewModel("purchases", "خرید و تأمین", "IconTruck"));
        Tabs.Add(new InventorySubnavViewModel("settings", "تنظیمات کالاها", "IconTools"));

        UseLoadStatus(ReloadPageAsync);
        // Stock moves with sales, payments and purchases.
        // A hidden page is never reloaded behind the user's back (the base class gates on visibility); the
        // entry hook re-reads on the next visit, and a visible refresh re-reads only the rows already shown.
        RefreshOnDataChange(changes, () => _ = ReloadPageAsync(), DataDomain.Sales, DataDomain.Cashier, DataDomain.Purchasing);
    }

    /// <summary>products | purchases | settings — the reference's <c>state.tab</c>.</summary>
    [ObservableProperty]
    private string _activeSubnav = "products";

    public bool IsProductsTab => ActiveSubnav == "products";
    public bool IsSettingsTab => ActiveSubnav == "settings";
    public bool IsPurchasesTab => ActiveSubnav == "purchases";

    /// <summary>The header «کالای جدید» button only shows on the products tab (reference bindHeaderNew).</summary>
    public bool ShowHeaderNew => IsProductsTab;

    /// <summary>The header «ثبت فاکتور خرید» button shows on the purchases tab.</summary>
    public bool ShowHeaderPurchase => IsPurchasesTab;

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

    // ── Pagination (mirrors PatientsPageViewModel) ─────────────────────────
    public IReadOnlyList<InventoryPageSizeOption> PageSizeOptions { get; } =
    [
        new(25, "۲۵ کالا"),
        new(50, "۵۰ کالا"),
        new(100, "۱۰۰ کالا"),
        new(ServerMaxPageSize, "همه"),
    ];

    [ObservableProperty]
    private InventoryPageSizeOption _selectedPageSize = new(50, "۵۰ کالا");

    [ObservableProperty]
    private int _currentPage = 1;

    private int PageSize => Math.Clamp(SelectedPageSize?.Value ?? 50, 1, ServerMaxPageSize);

    /// <summary>Total number of pages for the active filters (>= 1).</summary>
    public int PageCount => _listTotal == 0 ? 1 : (int)Math.Ceiling(_listTotal / (double)PageSize);

    /// <summary>Only paginate when the matches span more than one page.</summary>
    public bool ShowPagination => HasProducts && PageCount > 1;

    public bool CanGoPrevious => CurrentPage > 1;
    public bool CanGoNext => CurrentPage < PageCount;

    /// <summary>«صفحه ۲ از ۵»</summary>
    public string PageIndicatorText =>
        $"صفحه {PersianNumberFormatter.ToPersianDigits(CurrentPage)} از {PersianNumberFormatter.ToPersianDigits(PageCount)}";

    /// <summary>«۵۱–۱۰۰ از ۲۴۰ کالا»</summary>
    public string PageRangeText
    {
        get
        {
            if (_listTotal == 0) return string.Empty;
            var from = ((CurrentPage - 1) * PageSize) + 1;
            var to = Math.Min(CurrentPage * PageSize, _listTotal);
            return $"{PersianNumberFormatter.ToPersianDigits(from)}–{PersianNumberFormatter.ToPersianDigits(to)}"
                 + $" از {PersianNumberFormatter.ToPersianDigits(_listTotal)} کالا";
        }
    }

    // Changing the page size snaps back to page 1 and re-queries.
    partial void OnSelectedPageSizeChanged(InventoryPageSizeOption value) => ReloadFromFilter();

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void NextPage()
    {
        if (!CanGoNext) return;
        CurrentPage++;
        _ = ReloadListAsync();
    }

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private void PreviousPage()
    {
        if (!CanGoPrevious) return;
        CurrentPage--;
        _ = ReloadListAsync();
    }

    private void NotifyPaginationDerived()
    {
        OnPropertyChanged(nameof(PageCount));
        OnPropertyChanged(nameof(ShowPagination));
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(PageIndicatorText));
        OnPropertyChanged(nameof(PageRangeText));
        NextPageCommand.NotifyCanExecuteChanged();
        PreviousPageCommand.NotifyCanExecuteChanged();
    }

    // ── Settings sub-view ───────────────────────────────────────────────────
    public RangeObservableCollection<InventoryCategoryRowViewModel> CategoryRows { get; } = [];

    [ObservableProperty]
    private string _settingsCountText = string.Empty;

    partial void OnActiveSubnavChanged(string value)
    {
        OnPropertyChanged(nameof(IsProductsTab));
        OnPropertyChanged(nameof(IsSettingsTab));
        OnPropertyChanged(nameof(IsPurchasesTab));
        OnPropertyChanged(nameof(ShowHeaderNew));
        OnPropertyChanged(nameof(ShowHeaderPurchase));
    }

    // ── Purchases («خرید و تأمین») sub-view ─────────────────────────────────
    public RangeObservableCollection<PurchaseRowViewModel> PurchaseRows { get; } = [];

    private PurchasingOverview? _purchaseOverview;

    [ObservableProperty] private bool _hasPurchases;
    [ObservableProperty] private string _purchaseMonthSpendText = "۰";
    [ObservableProperty] private string _purchaseSupplierDebtText = "۰";
    [ObservableProperty] private string _purchaseActiveOrdersText = "۰";
    [ObservableProperty] private string _purchaseReceiptCountText = "۰";
    [ObservableProperty] private string _purchaseSupplierCountText = "۰";

    /// <summary>
    /// Reads the purchases KPIs + recent invoices. A role without purchasing access (or a transient failure) must
    /// never take the whole inventory page down, so a business failure just leaves the tab empty.
    /// </summary>
    private async Task LoadPurchasesAsync()
    {
        try
        {
            _purchaseOverview = await _purchasing.GetOverviewAsync();
        }
        catch (InvalidOperationException)
        {
            _purchaseOverview = null;
        }
    }

    private void BuildPurchases()
    {
        var overview = _purchaseOverview;
        PurchaseRows.ReplaceAll((overview?.Receipts ?? []).Select(r => new PurchaseRowViewModel(r)));
        HasPurchases = PurchaseRows.Count > 0;

        PurchaseMonthSpendText = InventoryPresentation.Money(overview?.MonthSpend ?? 0);
        PurchaseSupplierDebtText = InventoryPresentation.Money(overview?.SupplierDebt ?? 0);
        PurchaseActiveOrdersText = PersianNumberFormatter.ToPersianDigits(overview?.ActiveOrders ?? 0);
        PurchaseReceiptCountText = PersianNumberFormatter.ToPersianDigits(overview?.ReceiptCount ?? 0);
        PurchaseSupplierCountText = PersianNumberFormatter.ToPersianDigits(overview?.SupplierCount ?? 0);
    }

    /// <summary>Re-reads just the purchases data after switching to the tab or recording an invoice.</summary>
    private async Task RefreshPurchasesAsync()
    {
        await LoadPurchasesAsync();
        BuildPurchases();
        BuildSubnavCounts();

        // The register is a second view of the same purchases — refresh it too when it has been opened and is
        // the one on screen.
        if (IsSupplierRegisterView && _suppliersLoaded)
        {
            await LoadSupplierRegisterAsync();
        }
    }

    /// <summary>
    /// Opens the supplier purchase-invoice modal: pick a supplier (or add one), add existing / brand-new goods with
    /// quantity + purchase cost, then record — stock, purchase price, History and statistics follow. The modal
    /// announces <see cref="DataDomain.Purchasing"/>, which this page already refreshes on.
    /// </summary>
    [RelayCommand]
    private void OpenPurchaseInvoice()
    {
        _dialogs.ShowModal(
            new PurchaseInvoiceModalViewModel(_purchasing, _service, _toast, _dialogs, _changes), 860);
    }

    /// <summary>
    /// «ثبت فاکتور خرید» from a supplier's profile: the same modal, opened with that supplier already selected
    /// (so the operator never re-picks the supplier they are looking at).
    /// </summary>
    [RelayCommand]
    private void OpenPurchaseInvoiceForSelectedSupplier()
    {
        if (string.IsNullOrEmpty(_selectedSupplierId))
        {
            return;
        }

        _dialogs.ShowModal(
            new PurchaseInvoiceModalViewModel(_purchasing, _service, _toast, _dialogs, _changes,
                supplierId: _selectedSupplierId), 860);
    }

    // ── «تأمین‌کنندگان» sub-view (inside خرید و تأمین) ────────────────────────

    /// <summary>The active purchases sub-view: <c>invoices</c> (recorded purchase invoices) or <c>suppliers</c>.</summary>
    [ObservableProperty]
    private string _purchaseView = "invoices";

    public bool IsPurchaseInvoicesView => PurchaseView == "invoices";

    public bool IsSupplierRegisterView => PurchaseView == "suppliers";

    partial void OnPurchaseViewChanged(string value)
    {
        OnPropertyChanged(nameof(IsPurchaseInvoicesView));
        OnPropertyChanged(nameof(IsSupplierRegisterView));
    }

    /// <summary>Switches the purchases sub-view; the supplier register loads on its first activation.</summary>
    [RelayCommand]
    private void SetPurchaseView(string? key)
    {
        var next = key == "suppliers" ? "suppliers" : "invoices";
        if (next == PurchaseView)
        {
            return;
        }

        PurchaseView = next;
        if (IsSupplierRegisterView)
        {
            // A purchase recorded elsewhere changes the register, so re-read it on every activation.
            _ = LoadSupplierRegisterAsync();
        }
    }

    public RangeObservableCollection<SupplierRegisterRowViewModel> SupplierRows { get; } = [];

    public RangeObservableCollection<SupplierPurchaseRowViewModel> SupplierPurchases { get; } = [];

    [ObservableProperty] private bool _hasSuppliers;
    [ObservableProperty] private bool _hasNoSuppliers;
    [ObservableProperty] private string _supplierSearchText = string.Empty;
    [ObservableProperty] private bool _hasSupplierSearchValue;

    // ── selected supplier profile ───────────────────────────────────────────
    [ObservableProperty] private bool _hasSelectedSupplier;
    [ObservableProperty] private string _selectedSupplierName = string.Empty;
    [ObservableProperty] private string _selectedSupplierCode = string.Empty;
    [ObservableProperty] private string _selectedSupplierPhone = string.Empty;
    [ObservableProperty] private bool _selectedSupplierHasPhone;
    [ObservableProperty] private string _selectedSupplierTerm = string.Empty;
    [ObservableProperty] private string _selectedSupplierReceipts = "۰";
    [ObservableProperty] private string _selectedSupplierTotal = "۰";
    [ObservableProperty] private string _selectedSupplierLastPurchase = "—";
    [ObservableProperty] private bool _hasSupplierPurchases;
    [ObservableProperty] private string _supplierHistoryEmptyText = "برای این تأمین‌کننده هنوز خریدی ثبت نشده است";

    // ── add-supplier form ───────────────────────────────────────────────────
    [ObservableProperty] private string _newSupplierName = string.Empty;
    [ObservableProperty] private string _newSupplierPhone = string.Empty;
    [ObservableProperty] private string _supplierError = string.Empty;
    [ObservableProperty] private bool _isSupplierSaving;

    public bool HasSupplierError => SupplierError.Length > 0;

    partial void OnSupplierErrorChanged(string value) => OnPropertyChanged(nameof(HasSupplierError));

    private bool _suppliersLoaded;
    private string _selectedSupplierId = string.Empty;
    private readonly Debouncer _supplierSearchDebounce = new(System.TimeSpan.FromMilliseconds(180));

    partial void OnSupplierSearchTextChanged(string value)
    {
        HasSupplierSearchValue = !string.IsNullOrWhiteSpace(value);
        _supplierSearchDebounce.Debounce(() => _ = LoadSupplierRegisterAsync());
    }

    [RelayCommand]
    private void ClearSupplierSearch() => SupplierSearchText = string.Empty;

    /// <summary>
    /// Reads the supplier register (each supplier with its purchase statistics). A role without purchasing access
    /// (or a transient failure) must never take the page down, so a business failure just leaves the list empty.
    /// </summary>
    private async Task LoadSupplierRegisterAsync()
    {
        _suppliersLoaded = true;
        try
        {
            var search = string.IsNullOrWhiteSpace(SupplierSearchText) ? null : SupplierSearchText.Trim();
            var rows = await _purchasing.GetSuppliersAsync(search);
            SupplierRows.ReplaceAll(rows.Select(r => new SupplierRegisterRowViewModel(r)));
            HasSuppliers = SupplierRows.Count > 0;
            HasNoSuppliers = SupplierRows.Count == 0;

            // Keep the open profile on the same supplier when it is still in the filtered list.
            var keep = SupplierRows.FirstOrDefault(r => r.Id == _selectedSupplierId);
            if (keep is not null)
            {
                keep.IsActive = true;
            }
            else if (SupplierRows.Count > 0)
            {
                await SelectSupplierAsync(SupplierRows[0].Id);
            }
            else
            {
                ClearSupplierProfile();
            }
        }
        catch (InvalidOperationException)
        {
            SupplierRows.Clear();
            HasSuppliers = false;
            HasNoSuppliers = true;
            ClearSupplierProfile();
        }
    }

    /// <summary>Opens one supplier's profile: its identity + purchase statistics and its completed-purchase history.</summary>
    [RelayCommand]
    private async Task SelectSupplier(string? id) => await SelectSupplierAsync(id);

    private async Task SelectSupplierAsync(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        _selectedSupplierId = id;
        foreach (var row in SupplierRows)
        {
            row.IsActive = row.Id == id;
        }

        SupplierProfile? profile;
        try
        {
            profile = await _purchasing.GetSupplierProfileAsync(id);
        }
        catch (InvalidOperationException)
        {
            profile = null;
        }

        if (profile is null)
        {
            ClearSupplierProfile();
            return;
        }

        var summary = profile.Summary;
        HasSelectedSupplier = true;
        SelectedSupplierName = summary.Name;
        SelectedSupplierCode = string.IsNullOrWhiteSpace(summary.Code) ? "—" : summary.Code;
        SelectedSupplierPhone = string.IsNullOrWhiteSpace(summary.Phone) ? "شماره‌ای ثبت نشده" : summary.Phone;
        SelectedSupplierHasPhone = !string.IsNullOrWhiteSpace(summary.Phone);
        SelectedSupplierTerm = summary.PaymentTermDays <= 0
            ? "نقدی"
            : PersianNumberFormatter.ToPersianDigits(summary.PaymentTermDays) + " روز";
        SelectedSupplierReceipts = PersianNumberFormatter.ToPersianDigits(summary.ReceiptCount);
        SelectedSupplierTotal = InventoryPresentation.Money(summary.TotalPurchased);
        SelectedSupplierLastPurchase = string.IsNullOrWhiteSpace(summary.LastPurchaseJalali)
            ? "بدون خرید"
            : JalaliCalendar.Short(summary.LastPurchaseJalali);

        SupplierPurchases.ReplaceAll(profile.Purchases.Select(p => new SupplierPurchaseRowViewModel(p)));
        HasSupplierPurchases = SupplierPurchases.Count > 0;
        SupplierHistoryEmptyText = "برای این تأمین‌کننده هنوز خریدی ثبت نشده است";
    }

    private void ClearSupplierProfile()
    {
        _selectedSupplierId = string.Empty;
        HasSelectedSupplier = false;
        SelectedSupplierName = string.Empty;
        SelectedSupplierCode = string.Empty;
        SelectedSupplierPhone = string.Empty;
        SelectedSupplierHasPhone = false;
        SelectedSupplierTerm = string.Empty;
        SelectedSupplierReceipts = "۰";
        SelectedSupplierTotal = "۰";
        SelectedSupplierLastPurchase = "—";
        SupplierPurchases.ReplaceAll([]);
        HasSupplierPurchases = false;
    }

    /// <summary>
    /// Adds a supplier (name + optional phone) straight from the register, so the phone can be recorded without
    /// going through a purchase invoice. Duplicate names resolve to the existing supplier (the backend is
    /// get-or-create), and the new supplier is selected at once.
    /// </summary>
    [RelayCommand]
    private async Task AddSupplierAsync()
    {
        if (IsSupplierSaving)
        {
            return;
        }

        var name = NewSupplierName.Trim();
        if (name.Length == 0)
        {
            SupplierError = "نام تأمین‌کننده را وارد کنید.";
            return;
        }

        if (name.Length > 128)
        {
            SupplierError = "نام تأمین‌کننده نباید بیشتر از ۱۲۸ نویسه باشد.";
            return;
        }

        var phone = PersianNumberFormatter.ToAsciiDigits(NewSupplierPhone).Trim();
        if (phone.Length is > 0 and < 4)
        {
            SupplierError = "شمارهٔ تماس معتبر نیست.";
            return;
        }

        IsSupplierSaving = true;
        SupplierError = string.Empty;
        try
        {
            var created = await _purchasing.CreateSupplierAsync(name, phone.Length == 0 ? null : phone);
            NewSupplierName = string.Empty;
            NewSupplierPhone = string.Empty;
            _selectedSupplierId = created.Id;
            await LoadSupplierRegisterAsync();
            await SelectSupplierAsync(created.Id);
            _toast.Show($"تأمین‌کننده «{name}» ثبت شد", Tone.Ok);
            _changes.Notify(DataDomain.Purchasing);
        }
        catch (InvalidOperationException ex)
        {
            SupplierError = InventoryPresentation.MutationFailureMessage(
                ex, "ثبت تأمین‌کننده ناموفق بود؛ دوباره تلاش کنید.");
        }
        finally
        {
            IsSupplierSaving = false;
        }
    }


    // Singleton page: reload on every visit (not only at construction) so data changed elsewhere is never stale.
    // Failures surface in the shell's load-state block, which also offers the manual refresh.
    public override void OnNavigatedTo()
    {
        if (!IsLoadInFlight) _ = ReloadPageAsync();
    }

    // F-05: free the large per-visit result rows on a singleton page when leaving; OnNavigatedTo re-queries.
    public override void OnNavigatedFrom()
    {
        FilteredProducts.Clear();
        CategoryRows.Clear();
        PurchaseRows.Clear();
        SupplierRows.Clear();
        SupplierPurchases.Clear();
        _suppliersLoaded = false;
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

            await FetchListAsync();
            await LoadPurchasesAsync();
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
        BuildPurchases();
    }

    /// <summary>
    /// Fetches exactly the current page of the active filters' rows. Filtering, ordering and paging all run
    /// in SQL; a generation token drops a slower stale response. The previous window is always replaced.
    /// </summary>
    private async Task FetchListAsync()
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

        var page = await _service.GetProductsAsync(new ProductListQuery(search, category, stock, CurrentPage, PageSize));

        if (token != _listGeneration)
        {
            return; // a newer query superseded this one - drop the stale result
        }

        _loaded.Clear();
        _loaded.AddRange(page.Items);
        _listTotal = page.TotalCount;

        // Clamp into range now that the real total is known (e.g. a filter shrank the result set below
        // the current page index) and re-fetch that last valid page.
        var clamped = Math.Clamp(CurrentPage, 1, Math.Max(1, PageCount));
        if (clamped != CurrentPage)
        {
            CurrentPage = clamped;
            await FetchListAsync();
            return;
        }
        // The sub-nav's catalog size is the sum of the per-category counts already loaded — no extra query.
        _catalogTotal = _categories.Sum(c => c.ProductCount);
    }

    /// <summary>Re-queries after a filter / search / page change and re-renders the list (not the settings tab).</summary>
    private async Task ReloadListAsync()
    {
        try
        {
            await FetchListAsync();
            BuildSubnavCounts();
            RenderProducts();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _toast.Show(
                ex is InvalidOperationException ioe
                    ? InventoryPresentation.MutationFailureMessage(ioe, "بارگذاری فهرست کالاها ناموفق بود؛ دوباره تلاش کنید")
                    : "بارگذاری فهرست کالاها ناموفق بود؛ دوباره تلاش کنید",
                Tone.Danger);
        }
    }

    /// <summary>A filter / search / page-size change: snap back to page 1 and re-query.</summary>
    private void ReloadFromFilter()
    {
        CurrentPage = 1;
        _ = ReloadListAsync();
    }

    private void BuildSubnavCounts()
    {
        foreach (var tab in Tabs)
        {
            var count = tab.Key switch
            {
                "products" => _catalogTotal,
                "purchases" => _purchaseOverview?.ReceiptCount ?? 0,
                _ => _categories.Count,
            };
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
                           ?? (_categories.Count > 0 ? _categories[0] : new CategoryInfo(product.CategoryId, string.Empty, "teal"));
            var hasVariants = product.Variants.Count > 1;
            return new InventoryProductRowViewModel(product, category, hasVariants);
        }));

        HasProducts = FilteredProducts.Count > 0;
        NotifyPaginationDerived();
    }

    private void BuildSettings()
    {
        CategoryRows.ReplaceAll(_categories.Select(category =>
        {
            return new InventoryCategoryRowViewModel(
                category.Id, category.Label, category.Tone, category.ProductCount);
        }));

        SettingsCountText =
            $"{PersianNumberFormatter.ToPersianDigits(_categories.Count)} دسته فعال · {PersianNumberFormatter.ToPersianDigits(_catalogTotal)} کالا";
    }

    // ── category management (settings tab) ──────────────────────────────────
    [ObservableProperty]
    private string _newCategoryName = string.Empty;

    [RelayCommand]
    private async Task AddCategory()
    {
        var name = NewCategoryName.Trim();
        if (name.Length == 0)
        {
            _toast.Show("نام دسته را وارد کنید", Tone.Danger);
            return;
        }

        if (await MutateCategoryAsync(() => _service.CreateCategoryAsync(name), "افزودن دسته ناموفق بود", "دسته اضافه شد"))
        {
            NewCategoryName = string.Empty;
        }
    }

    [RelayCommand]
    private void BeginRenameCategory(InventoryCategoryRowViewModel? row)
    {
        if (row is null) return;
        foreach (var other in CategoryRows) other.IsEditing = false;
        row.EditName = row.Label;
        row.IsEditing = true;
    }

    [RelayCommand]
    private void CancelRenameCategory(InventoryCategoryRowViewModel? row)
    {
        if (row is not null) row.IsEditing = false;
    }

    [RelayCommand]
    private async Task SaveRenameCategory(InventoryCategoryRowViewModel? row)
    {
        if (row is null) return;
        var name = row.EditName.Trim();
        if (name.Length == 0)
        {
            _toast.Show("نام دسته را وارد کنید", Tone.Danger);
            return;
        }

        if (name == row.Label)
        {
            row.IsEditing = false;
            return;
        }

        await MutateCategoryAsync(() => _service.RenameCategoryAsync(row.Id, name), "تغییر نام دسته ناموفق بود", "نام دسته به‌روز شد");
    }

    [RelayCommand]
    private void DeleteCategory(InventoryCategoryRowViewModel? row)
    {
        if (row is null) return;
        if (!row.CanDelete)
        {
            _toast.Show("این دسته هنوز کالا دارد؛ ابتدا کالاها را به دسته دیگری منتقل کنید", Tone.Danger);
            return;
        }

        _dialogs.ShowModal(new Animora.Desktop.Pages.Sales.Shared.ConfirmDialogViewModel(
            _dialogs, "danger", "IconTrash", "حذف دسته", "دسته ", row.Label, " حذف شود؟",
            "این عمل قابل بازگشت نیست.", "حذف", "انصراف",
            () => _ = MutateCategoryAsync(() => _service.DeleteCategoryAsync(row.Id), "حذف دسته ناموفق بود", "دسته حذف شد")), 480);
    }

    /// <summary>Runs one category mutation, toasts the outcome and reloads categories + list on success.</summary>
    private async Task<bool> MutateCategoryAsync(Func<Task> action, string failure, string success)
    {
        try
        {
            await action();
        }
        catch (InvalidOperationException ex)
        {
            _toast.Show(InventoryPresentation.MutationFailureMessage(ex, failure), Tone.Danger);
            return false;
        }

        _toast.Show(success, Tone.Ok);
        Refresh();
        return true;
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

        if (key == "purchases")
        {
            _ = RefreshPurchasesAsync();
            return;
        }

        ReloadFromFilter();
    }

    [RelayCommand]
    private void SetCategoryFilter(string? id)
    {
        if (string.IsNullOrEmpty(id)) return;
        _catFilter = id;
        BuildFilterChips();
        ReloadFromFilter();
    }

    [RelayCommand]
    private void SetStockFilter(string? key)
    {
        if (string.IsNullOrEmpty(key)) return;
        _stockFilter = key;
        BuildFilterChips();
        ReloadFromFilter();
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
        ReloadFromFilter();
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
        _ = ReloadSafeAsync();
    }

    /// <summary>Post-mutation reload that never leaves a faulting task unobserved: a failure surfaces as a danger toast.</summary>
    private async Task ReloadSafeAsync()
    {
        try
        {
            await ReloadAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _toast.Show(
                ex is InvalidOperationException ioe
                    ? InventoryPresentation.MutationFailureMessage(ioe, "به‌روزرسانی فهرست کالاها ناموفق بود؛ دوباره تلاش کنید")
                    : "به‌روزرسانی فهرست کالاها ناموفق بود؛ دوباره تلاش کنید",
                Tone.Danger);
        }
    }

    private async Task ReloadAsync()
    {
        IsBusy = true;
        try
        {
            _categories.Clear();
            _categories.AddRange(await _service.GetCategoriesAsync());
            await FetchListAsync();
            await LoadPurchasesAsync();
        }
        finally
        {
            IsBusy = false;
        }

        RenderAll();
    }
}
