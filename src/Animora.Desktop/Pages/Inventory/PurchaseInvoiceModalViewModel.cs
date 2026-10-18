using System.Collections.ObjectModel;
using Animora.Contracts.Inventory;
using Animora.Contracts.Purchasing;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Inventory;

/// <summary>
/// Backs the «ثبت فاکتور خرید» modal of the Inventory «خرید و تأمین» tab. The admin picks (or types) a supplier, adds
/// lines — existing inventory items (quantity 1 and purchase cost auto-filled from the item) or brand-new goods — and
/// records the invoice through <see cref="IPurchasingService"/>. Recording is one atomic receipt: stock increases, each
/// line's unit cost becomes the item's purchase price, and the event lands in the main History + the product history.
/// </summary>
public sealed partial class PurchaseInvoiceModalViewModel : ObservableObject
{
    private const int PickerSize = 8;

    private readonly IPurchasingService _purchasing;
    private readonly IInventoryQueryService _inventory;
    private readonly IToastService _toast;
    private readonly IDialogService _dialogs;
    private readonly IDataChangeNotifier _changes;
    private readonly Action? _onRecorded;
    private readonly Debouncer _supplierDebounce = new(TimeSpan.FromMilliseconds(220));
    private readonly Debouncer _productDebounce = new(TimeSpan.FromMilliseconds(220));
    private int _productGeneration;
    private IDisposable? _stockSubscription;

    public void ObserveStockChanges()
    {
        _stockSubscription?.Dispose();
        _stockSubscription = _changes.Subscribe(domain =>
        {
            if (domain is DataDomain.Inventory or DataDomain.Purchasing or DataDomain.Sales)
                _productDebounce.Debounce(() => _ = SearchProductsAsync(ProductSearch));
        });
    }

    public void StopObservingStockChanges()
    {
        _stockSubscription?.Dispose();
        _stockSubscription = null;
        _productDebounce.Cancel();
        _supplierDebounce.Cancel();
        _productGeneration++;
    }

    public PurchaseInvoiceModalViewModel(
        IPurchasingService purchasing,
        IInventoryQueryService inventory,
        IToastService toast,
        IDialogService dialogs,
        IDataChangeNotifier changes,
        Action? onRecorded = null,
        string? supplierId = null)
    {
        _purchasing = purchasing;
        _inventory = inventory;
        _toast = toast;
        _dialogs = dialogs;
        _changes = changes;
        _onRecorded = onRecorded;
        _preselectSupplierId = supplierId;

        _ = LoadSafeAsync();
    }

    // Set when the modal was opened from a supplier's profile: that supplier starts selected.
    private readonly string? _preselectSupplierId;

    // ── supplier ────────────────────────────────────────────────────────────
    public ObservableCollection<SupplierRef> Suppliers { get; } = [];

    [ObservableProperty] private SupplierRef? _selectedSupplier;
    [ObservableProperty] private string _supplierSearch = string.Empty;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsExistingSupplier))]
    private bool _isNewSupplier;

    [ObservableProperty] private string _newSupplierName = string.Empty;

    /// <summary>The new supplier's phone number (optional) — stored with the supplier the receipt creates.</summary>
    [ObservableProperty] private string _newSupplierPhone = string.Empty;

    [ObservableProperty] private string _invoiceReference = string.Empty;

    public bool IsExistingSupplier => !IsNewSupplier;

    public string SupplierModeLabel => IsNewSupplier ? "انتخاب از فهرست" : "تأمین‌کنندهٔ جدید";

    partial void OnIsNewSupplierChanged(bool value) => OnPropertyChanged(nameof(SupplierModeLabel));

    partial void OnSupplierSearchChanged(string value) =>
        _supplierDebounce.Debounce(() => _ = ReloadSuppliersAsync(value));

    [RelayCommand]
    private void ToggleSupplierMode() => IsNewSupplier = !IsNewSupplier;

    // ── item picker (existing inventory) ────────────────────────────────────
    public ObservableCollection<PurchaseProductOption> Matches { get; } = [];

    [ObservableProperty] private string _productSearch = string.Empty;
    [ObservableProperty] private bool _hasMatches;
    [ObservableProperty] private bool _noMatches;

    partial void OnProductSearchChanged(string value) =>
        _productDebounce.Debounce(() => _ = SearchProductsAsync(value));

    private async Task SearchProductsAsync(string text)
    {
        var term = text?.Trim() ?? string.Empty;
        var token = ++_productGeneration;
        if (term.Length == 0)
        {
            Matches.Clear();
            HasMatches = false;
            NoMatches = false;
            return;
        }

        try
        {
            var page = await _inventory.GetProductsAsync(
                new ProductListQuery(term, null, ProductStockFilter.All, 1, PickerSize));
            if (token != _productGeneration) return;

            Matches.Clear();
            foreach (var product in page.Items)
            {
                foreach (var variant in product.Variants) Matches.Add(new PurchaseProductOption(product, variant));
            }

            HasMatches = Matches.Count > 0;
            NoMatches = Matches.Count == 0;
        }
        catch (InvalidOperationException)
        {
            if (token != _productGeneration) return;
            Matches.Clear();
            HasMatches = false;
            NoMatches = true;
        }
    }

    /// <summary>Adds an existing variant as a line: quantity 1, unit cost = its current purchase price.</summary>
    [RelayCommand]
    private void AddExisting(PurchaseProductOption? option)
    {
        if (option is null) return;

        var existing = Lines.FirstOrDefault(l => !l.IsNew && l.VariantId == option.Variant.Id && l.ProductId == option.Product.Id);
        if (existing is not null)
        {
            existing.Increment();
        }
        else
        {
            Lines.Add(new PurchaseLineViewModel(
                option.Product.Id, option.Variant.Id, option.Title, option.Product.Code,
                option.Variant.Cost, 1, RecalculateTotals));
        }

        ProductSearch = string.Empty;
        Error = string.Empty;
        RecalculateTotals();
    }

    // ── brand-new goods ─────────────────────────────────────────────────────
    public ObservableCollection<CategoryOptionViewModel> CategoryOptions { get; } = [];

    [ObservableProperty] private bool _isNewProductOpen;
    [ObservableProperty] private string _newName = string.Empty;
    [ObservableProperty] private string _newCode = string.Empty;
    [ObservableProperty] private string _newSalePriceText = string.Empty;
    [ObservableProperty] private CategoryOptionViewModel? _newCategory;

    [RelayCommand]
    private void ToggleNewProduct() => IsNewProductOpen = !IsNewProductOpen;

    /// <summary>Queues a brand-new good; it is created in the catalog only when the invoice is recorded.</summary>
    [RelayCommand]
    private void AddNewProduct()
    {
        var name = NewName.Trim();
        if (name.Length == 0)
        {
            Error = "نام کالای جدید را وارد کنید.";
            return;
        }

        var code = NewCode.Trim();
        var category = NewCategory ?? CategoryOptions.FirstOrDefault();
        if (category is null)
        {
            Error = "ابتدا یک دسته برای کالا تعریف کنید.";
            return;
        }

        var price = long.TryParse(PersianNumberFormatter.ToAsciiDigits(NewSalePriceText), out var p) ? Math.Max(0, p) : 0;
        Lines.Add(new PurchaseLineViewModel(string.Empty, string.Empty, name, "کالای جدید", 0, 1, RecalculateTotals)
        {
            IsNew = true,
            NewCode = code,
            NewCategoryId = category.Id,
            NewSalePrice = price,
        });

        NewName = string.Empty;
        NewCode = string.Empty;
        NewSalePriceText = string.Empty;
        IsNewProductOpen = false;
        Error = string.Empty;
        RecalculateTotals();
    }

    // ── lines + totals ──────────────────────────────────────────────────────
    public ObservableCollection<PurchaseLineViewModel> Lines { get; } = [];

    [ObservableProperty] private string _grandTotalText = "۰";
    [ObservableProperty] private string _lineCountText = "۰";
    [ObservableProperty] private bool _hasLines;
    [ObservableProperty] private string _error = string.Empty;
    [ObservableProperty] private bool _isSaving;

    public bool HasError => Error.Length > 0;

    partial void OnErrorChanged(string value) => OnPropertyChanged(nameof(HasError));

    [RelayCommand]
    private void RemoveLine(PurchaseLineViewModel? line)
    {
        if (line is null) return;
        Lines.Remove(line);
        RecalculateTotals();
    }

    private void RecalculateTotals()
    {
        GrandTotalText = InventoryPresentation.Money(Lines.Sum(l => l.LineTotal));
        LineCountText = PersianNumberFormatter.ToPersianDigits(Lines.Count);
        HasLines = Lines.Count > 0;
    }

    // ── load ────────────────────────────────────────────────────────────────
    private async Task LoadSafeAsync()
    {
        try
        {
            foreach (var category in await _inventory.GetCategoriesAsync())
            {
                CategoryOptions.Add(new CategoryOptionViewModel(category));
            }

            NewCategory = CategoryOptions.FirstOrDefault();
            await ReloadSuppliersAsync(null);

            // Opened from a supplier's profile: keep that supplier selected instead of switching to «new».
            if (!string.IsNullOrEmpty(_preselectSupplierId)
                && Suppliers.FirstOrDefault(s => s.Id == _preselectSupplierId) is { } preselected)
            {
                SelectedSupplier = preselected;
                IsNewSupplier = false;
            }
            else
            {
                IsNewSupplier = Suppliers.Count == 0;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _toast.Show(
                ex is InvalidOperationException ioe
                    ? InventoryPresentation.MutationFailureMessage(ioe, "بارگذاری اطلاعات خرید ناموفق بود؛ دوباره تلاش کنید")
                    : "بارگذاری اطلاعات خرید ناموفق بود؛ دوباره تلاش کنید",
                Tone.Danger);
        }
    }

    private async Task ReloadSuppliersAsync(string? search)
    {
        try
        {
            var keep = SelectedSupplier?.Id;
            var found = await _purchasing.FindSuppliersAsync(search);
            Suppliers.Clear();
            foreach (var supplier in found) Suppliers.Add(supplier);
            SelectedSupplier = Suppliers.FirstOrDefault(s => s.Id == keep) ?? Suppliers.FirstOrDefault();
        }
        catch (InvalidOperationException)
        {
            Suppliers.Clear();
        }
    }

    // ── record ──────────────────────────────────────────────────────────────
    [RelayCommand]
    private void Cancel() => _dialogs.Close();

    [RelayCommand]
    private async Task RecordAsync()
    {
        if (IsSaving) return;
        Error = string.Empty;

        var supplierId = IsNewSupplier ? null : SelectedSupplier?.Id;
        var newSupplier = IsNewSupplier ? NewSupplierName.Trim() : null;
        if (string.IsNullOrEmpty(supplierId) && string.IsNullOrEmpty(newSupplier))
        {
            Error = "تأمین‌کننده را انتخاب کنید یا نام تأمین‌کنندهٔ جدید را بنویسید.";
            return;
        }

        var newSupplierPhone = IsNewSupplier ? PersianNumberFormatter.ToAsciiDigits(NewSupplierPhone).Trim() : null;
        if (newSupplierPhone is { Length: > 0 and < 4 })
        {
            Error = "شمارهٔ تماس تأمین‌کننده معتبر نیست.";
            return;
        }

        if (Lines.Count == 0)
        {
            Error = "حداقل یک کالا به فاکتور اضافه کنید.";
            return;
        }

        foreach (var line in Lines)
        {
            if (line.Quantity <= 0)
            {
                Error = $"تعداد «{line.Title}» باید بیشتر از صفر باشد.";
                return;
            }
        }

        IsSaving = true;
        try
        {
            // Brand-new goods are created first (idempotent per line: a retry after a failed receipt reuses the ids).
            foreach (var line in Lines.Where(l => l.IsNew && string.IsNullOrEmpty(l.ProductId)))
            {
                if (!await CreateNewProductAsync(line)) return;
            }

            var request = new RecordPurchaseRequest(
                supplierId,
                newSupplier,
                Lines.Select(l => new PurchaseLineInput(l.ProductId, l.VariantId, l.Quantity, l.UnitCost)).ToList(),
                string.IsNullOrWhiteSpace(InvoiceReference) ? null : InvoiceReference.Trim(),
                string.IsNullOrWhiteSpace(newSupplierPhone) ? null : newSupplierPhone);

            var result = await _purchasing.RecordPurchaseAsync(request);
            if (!result.Ok)
            {
                Error = Animora.Desktop.Localization.ErrorMessages.ForResult(
                    result.Error, "ثبت فاکتور خرید ناموفق بود؛ دوباره تلاش کنید.");
                return;
            }
        }
        catch (InvalidOperationException ex)
        {
            Error = InventoryPresentation.MutationFailureMessage(ex, "ثبت فاکتور خرید ناموفق بود؛ دوباره تلاش کنید.");
            return;
        }
        finally
        {
            IsSaving = false;
        }

        _dialogs.Close();
        _toast.Show("فاکتور خرید ثبت شد؛ موجودی و قیمت خرید کالاها به‌روز شد", Tone.Ok);
        _changes.Notify(DataDomain.Purchasing, DataDomain.Inventory, DataDomain.History, DataDomain.Finance, DataDomain.Dashboard);
        _onRecorded?.Invoke();
    }

    /// <summary>Creates a queued brand-new good (stock 0 — the receipt provides the quantity) and binds its ids to the line.</summary>
    private async Task<bool> CreateNewProductAsync(PurchaseLineViewModel line)
    {
        try
        {
            var code = line.NewCode;
            var cost = line.UnitCost;
            var created = await _inventory.CreateProductAsync(new NewProductRequest(
                code, line.Title, string.Empty, line.NewCategoryId, string.Empty, null, [],
                [
                    new ProductVariant(
                        "v_" + Guid.NewGuid().ToString("N")[..6], "تک واحد", code,
                        0, 5, cost, line.NewSalePrice, null),
                ],
                false, cost, line.NewSalePrice));

            var variant = created.Variants.FirstOrDefault();
            if (variant is null)
            {
                Error = $"ساخت کالای «{line.Title}» ناموفق بود؛ دوباره تلاش کنید.";
                return false;
            }

            line.ProductId = created.Id;
            line.VariantId = variant.Id;
            return true;
        }
        catch (InvalidOperationException ex)
        {
            Error = InventoryPresentation.MutationFailureMessage(ex, $"ساخت کالای «{line.Title}» ناموفق بود؛ دوباره تلاش کنید.");
            return false;
        }
    }
}
