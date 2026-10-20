using System.Collections.ObjectModel;
using Animora.Contracts.Inventory;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Services;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Inventory;

/// <summary>
/// Backs the Inventory product detail/edit modal (Animora-UI/assets/inventory.js
/// <c>openProductModal</c>/<c>renderProductModal</c>): the toned header
/// (thumbnail, «category · code» eyebrow, name, «brand · description» sub), the
/// Info|Variants|History tab strip (mirroring <c>renderInfoTab</c>/
/// <c>renderVariantsTab</c>/<c>renderHistoryTab</c>) and the footer actions
/// (ذخیره تغییرات / انصراف / حذف کالا). All data flows through
/// <see cref="IInventoryQueryService"/> (R-18).
/// </summary>
public sealed partial class ProductModalViewModel : ObservableObject
{
    private readonly IInventoryQueryService _service;
    private readonly IToastService _toast;
    private readonly IDialogService _dialogs;
    private readonly Action? _onChanged;

    private Product _product = null!;
    private UnitInfo? _attribute;
    private const int HistoryPageSize = 50;
    private readonly List<StockMovement> _movements = [];
    private int _movementTotal;
    private int _historyGeneration;
    private readonly List<InventoryHistoryItemViewModel> _allHistory = [];

    public ProductModalViewModel(
        IInventoryQueryService service,
        IToastService toast,
        IDialogService dialogs,
        string productId,
        Action? onChanged = null)
    {
        _service = service;
        _toast = toast;
        _dialogs = dialogs;
        _onChanged = onChanged;
        _ = LoadSafeAsync(productId);
    }

    // ── header ──────────────────────────────────────────────────────────────
    [ObservableProperty] private string _categoryLabel = string.Empty;
    [ObservableProperty] private string _code = string.Empty;

    /// <summary>Inline validation message under the code field (blank/duplicate SKU), empty when valid.</summary>
    [ObservableProperty] private string _codeError = string.Empty;

    public bool HasCodeError => CodeError.Length > 0;

    partial void OnCodeErrorChanged(string value) => OnPropertyChanged(nameof(HasCodeError));
    [ObservableProperty] private string _brand = string.Empty;
    [ObservableProperty] private string _description = string.Empty;

    [ObservableProperty] private string _name = string.Empty;

    /// <summary>The header sub-line: «brand · description» (reference <c>p.brand + ' · ' + p.description</c>).</summary>
    public string SubLine => string.IsNullOrWhiteSpace(Description) ? Brand : $"{Brand} · {Description}";

    /// <summary>The eyebrow line: «category · code» (reference <c>${cat.label} · ${p.code}</c>).</summary>
    public string EyebrowLine => $"{CategoryLabel} · {Code}";

    partial void OnBrandChanged(string value) => OnPropertyChanged(nameof(SubLine));
    partial void OnDescriptionChanged(string value) => OnPropertyChanged(nameof(SubLine));
    partial void OnCategoryLabelChanged(string value) => OnPropertyChanged(nameof(EyebrowLine));
    partial void OnCodeChanged(string value)
    {
        OnPropertyChanged(nameof(EyebrowLine));
        CodeError = string.Empty;
    }

    // ── tab strip ───────────────────────────────────────────────────────────
    [ObservableProperty] private string _activeTab = "info";

    public bool IsInfoTab => ActiveTab == "info";
    public bool IsVariantsTab => ActiveTab == "variants";
    public bool IsHistoryTab => ActiveTab == "history";

    partial void OnActiveTabChanged(string value)
    {
        OnPropertyChanged(nameof(IsInfoTab));
        OnPropertyChanged(nameof(IsVariantsTab));
        OnPropertyChanged(nameof(IsHistoryTab));

        // The ledger moves while the modal is open (sales, purchases, adjustments): re-read it each time the tab
        // is opened so the timeline never shows a stale snapshot.
        if (value == "history" && _product is not null) _ = ReloadHistorySafeAsync(append: false);
    }

    [RelayCommand]
    private void SetTab(string? key)
    {
        if (!string.IsNullOrEmpty(key))
        {
            ActiveTab = key;
        }
    }

    /// <summary>«افزودن تنوع» jump from the single-unit hint (reference <c>data-mtab-jump</c>).</summary>
    [RelayCommand]
    private void JumpToVariants() => ActiveTab = "variants";

    // ── Info tab: KPIs + base fields + single-unit price/stock ───────────────
    [ObservableProperty] private string _totalStockText = "۰";
    [ObservableProperty] private string _totalValueText = "۰";
    [ObservableProperty] private string _unitCountText = "۱";
    [ObservableProperty] private string _unitCountLabel = "تک واحد";

    public ObservableCollection<CategoryOptionViewModel> CategoryOptions { get; } = [];

    [ObservableProperty] private CategoryOptionViewModel? _selectedCategory;

    /// <summary>A product is "multi-variant" when it carries an attribute or more than one variant.</summary>
    public bool HasVariants => _attribute is not null || _variantCards.Count > 1;
    public bool IsSingleUnit => !HasVariants;

    public bool ShowSingleBlock => IsSingleUnit;

    [ObservableProperty] private string _singleStockText = "۰";
    [ObservableProperty] private string _singleReorderText = "۰";
    [ObservableProperty] private string _singleCostText = "۰";
    [ObservableProperty] private string _singlePriceText = "۰";
    [ObservableProperty] private string _singleExpiryText = string.Empty;

    // On-hand stock is a projection of stock movements: it is shown here read-only and changes only through
    // the «تغییر موجودی» modal (a reasoned movement), never through the product info save (P0-2).
    private int ParseSingleStock() =>
        int.TryParse(PersianNumberFormatter.ToAsciiDigits(SingleStockText), out var v) ? v : 0;

    // ── Variants tab ────────────────────────────────────────────────────────
    private readonly ObservableCollection<InventoryVariantCardViewModel> _variantCards = [];
    public ObservableCollection<InventoryVariantCardViewModel> VariantCards => _variantCards;

    public ObservableCollection<InventoryOptionChipViewModel> AttributeOptions { get; } = [];

    [ObservableProperty] private string _newAttributeName = string.Empty;
    [ObservableProperty] private string _newOptionText = string.Empty;

    public bool HasAttribute => _attribute is not null;
    public bool ShowVariantIntro => !HasAttribute && _variantCards.Count <= 1;
    public bool ShowVariantBuilder => HasAttribute;

    /// <summary>«گزینه‌ها (N)» label count.</summary>
    public string AttributeOptionsCount =>
        $"({PersianNumberFormatter.ToPersianDigits(AttributeOptions.Count)})";

    /// <summary>«تنوع‌ها (N)» heading count.</summary>
    public string VariantCountHeading =>
        $"({PersianNumberFormatter.ToPersianDigits(_variantCards.Count)})";

    public ObservableCollection<string> QuickAttributeSuggestions { get; } =
        new(["وزن", "حجم", "سایز", "دوز"]);

    [RelayCommand]
    private void EnableAttribute(string? name)
    {
        var nm = string.IsNullOrWhiteSpace(name) ? "ویژگی" : name.Trim();
        _attribute = new UnitInfo(nm, []);
        NewAttributeName = nm;
        SyncVariantsToOptions();
        RaiseVariantState();
    }

    [RelayCommand]
    private void DisableAttribute()
    {
        _attribute = null;
        if (_variantCards.Count > 1)
        {
            var keep = _variantCards[0];
            _variantCards.Clear();
            _variantCards.Add(keep);
        }

        if (_variantCards.Count == 0)
        {
            AddCard(new ProductVariant(
                "v_" + Guid.NewGuid().ToString("N")[..6], "تک واحد", Code, 0, 5, 0, 0, null));
        }
        else if (string.IsNullOrWhiteSpace(_variantCards[0].Label))
        {
            _variantCards[0].Label = "تک واحد";
        }

        RebuildAttributeOptions();
        RaiseVariantState();
    }

    partial void OnNewAttributeNameChanged(string value)
    {
        if (_attribute is not null)
        {
            _attribute = _attribute with { Name = value };
        }
    }

    [RelayCommand]
    private void AddOption()
    {
        if (_attribute is null)
        {
            return;
        }

        var value = NewOptionText.Trim();
        NewOptionText = string.Empty;
        if (value.Length == 0)
        {
            return;
        }

        var options = _attribute.Options.ToList();
        if (!options.Contains(value))
        {
            options.Add(value);
            _attribute = _attribute with { Options = options };
            SyncVariantsToOptions();
            RaiseVariantState();
        }
    }

    [RelayCommand]
    private void RemoveOption(InventoryOptionChipViewModel? chip)
    {
        if (chip is null || _attribute is null)
        {
            return;
        }

        _attribute = _attribute with { Options = _attribute.Options.Where(o => o != chip.Label).ToList() };
        SyncVariantsToOptions();
        RaiseVariantState();
    }

    [RelayCommand]
    private void AddManualVariant()
    {
        var variant = MakeVariant("تنوع جدید", _variantCards.Count);
        AddCard(variant);
        if (_attribute is not null && !_attribute.Options.Contains(variant.Label))
        {
            _attribute = _attribute with { Options = _attribute.Options.Append(variant.Label).ToList() };
            RebuildAttributeOptions();
        }

        RaiseVariantState();
    }

    [RelayCommand]
    private void DeleteVariant(InventoryVariantCardViewModel? card)
    {
        if (card is null)
        {
            return;
        }

        if (_variantCards.Count <= 1)
        {
            _toast.Show("حداقل یک تنوع لازم است", Tone.Warn);
            return;
        }

        _variantCards.Remove(card);
        if (_attribute is not null)
        {
            _attribute = _attribute with { Options = _attribute.Options.Where(o => o != card.Label).ToList() };
            RebuildAttributeOptions();
        }

        RaiseVariantState();
    }

    // Keep existing variants whose label matches an option; create new ones for the rest.
    private void SyncVariantsToOptions()
    {
        if (_attribute is null)
        {
            return;
        }

        var byLabel = _variantCards.ToDictionary(v => v.Label, v => v);
        var rebuilt = new List<InventoryVariantCardViewModel>();
        var index = 0;
        foreach (var option in _attribute.Options)
        {
            rebuilt.Add(byLabel.TryGetValue(option, out var existing)
                ? existing
                : MakeCard(MakeVariant(option, index)));
            index++;
        }

        if (rebuilt.Count == 0)
        {
            rebuilt.Add(MakeCard(MakeVariant(_attribute.Name.Length > 0 ? _attribute.Name : "تنوع ۱", 0)));
        }

        _variantCards.Clear();
        foreach (var card in rebuilt)
        {
            _variantCards.Add(card);
        }
    }

    private void RebuildAttributeOptions()
    {
        AttributeOptions.Clear();
        foreach (var option in _attribute?.Options ?? [])
        {
            AttributeOptions.Add(new InventoryOptionChipViewModel(option));
        }

        OnPropertyChanged(nameof(AttributeOptionsCount));
    }

    private ProductVariant MakeVariant(string label, int index) => new(
        "v_" + Guid.NewGuid().ToString("N")[..6],
        label,
        UniqueVariantSku(index),
        0, 5, 0, 0, null);

    /// <summary>A variant SKU not already used by a card in this modal (deletions can leave index gaps).</summary>
    private string UniqueVariantSku(int index)
    {
        for (var n = index + 1; n < index + 1000; n++)
        {
            var candidate = $"{Code}-{n.ToString().PadLeft(2, '0')}";
            if (!_variantCards.Any(c => string.Equals(c.Sku, candidate, StringComparison.Ordinal))) return candidate;
        }

        return $"{Code}-{Guid.NewGuid().ToString("N")[..4]}";
    }

    private InventoryVariantCardViewModel MakeCard(ProductVariant variant)
    {
        var card = new InventoryVariantCardViewModel(variant);
        return card;
    }

    private void AddCard(ProductVariant variant) => _variantCards.Add(MakeCard(variant));

    private void RaiseVariantState()
    {
        OnPropertyChanged(nameof(HasVariants));
        OnPropertyChanged(nameof(IsSingleUnit));
        OnPropertyChanged(nameof(ShowSingleBlock));
        OnPropertyChanged(nameof(ShowVariantIntro));
        OnPropertyChanged(nameof(ShowVariantBuilder));
        OnPropertyChanged(nameof(VariantCountHeading));
    }

    // ── History tab ─────────────────────────────────────────────────────────
    public ObservableCollection<InventoryHistoryItemViewModel> HistoryItems { get; } = [];

    [ObservableProperty] private string _activeHistoryFilter = "all";

    [ObservableProperty] private string _historyTotalInText = "+۰";
    [ObservableProperty] private string _historyTotalOutText = "−۰";
    [ObservableProperty] private string _historyAdjustCountText = "۰";

    [ObservableProperty] private bool _hasHistory;

    public bool HistoryFilterAll => ActiveHistoryFilter == "all";
    public bool HistoryFilterIn => ActiveHistoryFilter == "in";
    public bool HistoryFilterOut => ActiveHistoryFilter == "out";
    public bool HistoryFilterAdjust => ActiveHistoryFilter == "adjust";

    partial void OnActiveHistoryFilterChanged(string value)
    {
        OnPropertyChanged(nameof(HistoryFilterAll));
        OnPropertyChanged(nameof(HistoryFilterIn));
        OnPropertyChanged(nameof(HistoryFilterOut));
        OnPropertyChanged(nameof(HistoryFilterAdjust));
        ApplyHistoryFilter();
    }

    [RelayCommand]
    private void SetHistoryFilter(string? key)
    {
        if (!string.IsNullOrEmpty(key))
        {
            ActiveHistoryFilter = key;
        }
    }

    private void ApplyHistoryFilter()
    {
        HistoryItems.Clear();
        foreach (var item in _allHistory)
        {
            if (ActiveHistoryFilter == "all" || item.Kind == ActiveHistoryFilter)
            {
                HistoryItems.Add(item);
            }
        }

        HasHistory = HistoryItems.Count > 0;
    }

    // ── load / save / delete ────────────────────────────────────────────────
    /// <summary>
    /// Observed initial load: a seam failure (<see cref="InvalidOperationException"/> /
    /// <c>ServiceException</c>) surfaces as a danger toast and closes the modal instead of
    /// escaping the constructor's fire-and-forget task (X09 guard precedent, P2-9).
    /// </summary>
    private async Task LoadSafeAsync(string productId)
    {
        try
        {
            await LoadAsync(productId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _toast.Show(
                ex is InvalidOperationException ioe
                    ? InventoryPresentation.MutationFailureMessage(ioe, "بارگذاری جزئیات کالا ناموفق بود؛ دوباره تلاش کنید")
                    : "بارگذاری جزئیات کالا ناموفق بود؛ دوباره تلاش کنید",
                Tone.Danger);
            _dialogs.Close();
        }
    }

    private async Task LoadAsync(string productId)
    {
        var detail = await _service.GetDetailAsync(productId);
        if (detail is null)
        {
            _dialogs.Close();
            return;
        }

        _product = detail.Product;
        _attribute = _product.Attribute;
        foreach (var category in await _service.GetCategoriesAsync())
        {
            CategoryOptions.Add(new CategoryOptionViewModel(category));
        }

        // header
        CategoryLabel = CategoryOptions.FirstOrDefault(c => c.Id == _product.CategoryId)?.Label ?? string.Empty;
        Code = _product.Code;
        Name = _product.Name;
        Brand = _product.Brand;
        Description = _product.Description;
        SelectedCategory = CategoryOptions.FirstOrDefault(c => c.Id == _product.CategoryId);

        // variant cards
        _variantCards.Clear();
        foreach (var variant in _product.Variants)
        {
            AddCard(variant);
        }

        NewAttributeName = _attribute?.Name ?? string.Empty;
        RebuildAttributeOptions();

        // single-unit block
        var single = IsSingleUnit ? _variantCards.FirstOrDefault() : null;
        if (single is not null)
        {
            SingleStockText = single.StockText;
            SingleReorderText = single.ReorderText;
            SingleCostText = single.CostText;
            SinglePriceText = single.PriceText;
            SingleExpiryText = single.ExpiryText;
        }

        RebuildKpis();
        await ReloadHistorySafeAsync(append: false);
        RaiseVariantState();
    }

    /// <summary>
    /// Reads the movement ledger page by page: the first page (or as many rows as are already shown, so a refresh
    /// keeps the user's place) or, with <paramref name="append"/>, the next page. A generation token drops a
    /// slower stale response.
    /// </summary>
    private async Task ReloadHistoryAsync(bool append)
    {
        var token = ++_historyGeneration;
        MovementPage page;
        if (append)
        {
            page = await _service.GetHistoryAsync(_product.Id, _movements.Count / HistoryPageSize + 1, HistoryPageSize);
        }
        else
        {
            var size = Math.Clamp(_movements.Count, HistoryPageSize, 200);
            page = await _service.GetHistoryAsync(_product.Id, 1, size);
        }

        if (token != _historyGeneration) return;

        if (!append) _movements.Clear();
        _movements.AddRange(page.Items);
        _movementTotal = page.TotalCount;
        BuildHistory();
    }

    private async Task ReloadHistorySafeAsync(bool append)
    {
        try
        {
            await ReloadHistoryAsync(append);
        }
        catch (InvalidOperationException ex)
        {
            _toast.Show(InventoryPresentation.MutationFailureMessage(ex, "بارگذاری تاریخچه کالا ناموفق بود؛ دوباره تلاش کنید"), Tone.Danger);
        }
    }

    /// <summary>Whether older movements exist beyond the pages already loaded.</summary>
    public bool CanLoadMoreHistory => _movements.Count < _movementTotal;

    /// <summary>«نشان‌داده‌شده N از M» - the loaded window versus the whole ledger.</summary>
    public string HistoryWindowText => _movementTotal == 0
        ? string.Empty
        : $"نمایش {PersianNumberFormatter.ToPersianDigits(_movements.Count)} از {PersianNumberFormatter.ToPersianDigits(_movementTotal)} رخداد";

    [RelayCommand]
    private Task LoadMoreHistory() => ReloadHistorySafeAsync(append: true);

    private void RebuildKpis()
    {
        var variants = CurrentVariants();
        var stock = variants.Sum(v => v.Stock);
        var value = variants.Sum(v => v.Stock * v.Cost);

        TotalStockText = InventoryPresentation.Money(stock);
        TotalValueText = $"{InventoryPresentation.Money(value)} ت";
        UnitCountText = PersianNumberFormatter.ToPersianDigits(HasVariants ? variants.Count : 1);
        UnitCountLabel = HasVariants ? "تنوع فعال" : "تک واحد";
    }

    private void BuildHistory()
    {
        _allHistory.Clear();
        var variantLabels = _product.Variants.ToDictionary(v => v.Id, v => v.Label);
        foreach (var movement in _movements)
        {
            var label = movement.VariantId is not null && variantLabels.TryGetValue(movement.VariantId, out var l)
                ? l
                : string.Empty;
            _allHistory.Add(new InventoryHistoryItemViewModel(movement, label));
        }

        var totalIn = _movements.Where(m => m.Kind == "in").Sum(m => m.Qty);
        var totalOut = _movements.Where(m => m.Kind == "out").Sum(m => m.Qty);
        var adjustCount = _movements.Count(m => m.Kind == "adjust");
        HistoryTotalInText = "+" + InventoryPresentation.Money(totalIn);
        HistoryTotalOutText = "−" + InventoryPresentation.Money(totalOut);
        HistoryAdjustCountText = PersianNumberFormatter.ToPersianDigits(adjustCount);

        ApplyHistoryFilter();
        OnPropertyChanged(nameof(CanLoadMoreHistory));
        OnPropertyChanged(nameof(HistoryWindowText));
    }

    /// <summary>The edited variants projected from the current fields (single block on a single product).</summary>
    private List<ProductVariant> CurrentVariants()
    {
        if (IsSingleUnit)
        {
            var card = _variantCards.FirstOrDefault();
            var id = card?.Id ?? "v_" + Guid.NewGuid().ToString("N")[..6];
            var label = string.IsNullOrWhiteSpace(card?.Label) ? "تک واحد" : card!.Label;
            var sku = card?.Sku ?? Code;
            return
            [
                new ProductVariant(
                    id, label, sku,
                    ParseSingleStock(),
                    ParseInt(SingleReorderText),
                    ParseLong(SingleCostText),
                    ParseLong(SinglePriceText),
                    string.IsNullOrWhiteSpace(SingleExpiryText) ? null : SingleExpiryText.Trim()),
            ];
        }

        return _variantCards.Select(v => v.ToVariant()).ToList();
    }

    private static int ParseInt(string text) =>
        int.TryParse(PersianNumberFormatter.ToAsciiDigits(text), out var v) ? v : 0;

    private static long ParseLong(string text) =>
        long.TryParse(PersianNumberFormatter.ToAsciiDigits(text), out var v) ? v : 0;

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (Name.Trim().Length == 0)
        {
            _toast.Show("نام کالا را وارد کنید", Tone.Danger);
            return;
        }

        var variants = CurrentVariants();
        var codeError = ValidateSkus(variants);
        CodeError = codeError ?? string.Empty;
        if (codeError is not null)
        {
            _toast.Show(codeError, Tone.Danger);
            return;
        }

        var update = new ProductInfoUpdate(
            Name.Trim(),
            Code.Trim(),
            Brand.Trim(),
            SelectedCategory?.Id ?? _product.CategoryId,
            Description.Trim(),
            HasAttribute ? _attribute?.Name : null,
            variants);

        try
        {
            await _service.SaveProductInfoAsync(_product.Id, update);
        }
        catch (InvalidOperationException ex)
        {
            // Keep the modal open so the edits are not lost; never claim success.
            if (ex is Animora.Client.Services.ServiceException { Code: "PRODUCT_SKU_DUPLICATE" })
            {
                CodeError = "این کد قبلاً استفاده شده است";
            }

            _toast.Show(InventoryPresentation.MutationFailureMessage(ex, "ذخیره تغییرات ناموفق بود؛ دوباره تلاش کنید"), Tone.Danger);
            return;
        }

        _dialogs.Close();
        _toast.Show("تغییرات ذخیره شد", Tone.Ok);
        _onChanged?.Invoke();
    }

    /// <summary>Client-side SKU guard: the product code and every variant SKU must be present and unique in the modal.</summary>
    private string? ValidateSkus(List<ProductVariant> variants)
    {
        if (Code.Trim().Length == 0) return "کد کالا را وارد کنید";

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var variant in variants)
        {
            var sku = variant.Sku.Trim();
            if (sku.Length == 0) return "کد تنوع نمی‌تواند خالی باشد";
            if (!seen.Add(sku)) return "کد تنوع‌ها نباید تکراری باشد";
        }

        return null;
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        try
        {
            await _service.DeleteProductAsync(_product.Id);
        }
        catch (InvalidOperationException ex)
        {
            _toast.Show(InventoryPresentation.MutationFailureMessage(ex, "حذف کالا ناموفق بود؛ دوباره تلاش کنید"), Tone.Danger);
            return;
        }

        _dialogs.Close();
        _toast.Show("کالا حذف شد", Tone.Ok);
        _onChanged?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => _dialogs.Close();

    /// <summary>
    /// Opens the «تغییر موجودی» (stock-adjust) modal for this product so the quantity can be raised or
    /// lowered through a reasoned stock movement — the on-hand field in this modal is intentionally
    /// read-only (on-hand is never hand-edited; it only moves via purchases, sales or audited
    /// adjustments, P0-2). The single modal host replaces this modal with the adjust modal; once the
    /// movement commits we reopen the product modal reloaded so the new stock shows, and bubble the
    /// change to the inventory list.
    /// </summary>
    [RelayCommand]
    private void OpenStockAdjust()
    {
        // The command is only reachable once the detail (and therefore _product) has loaded, but guard the
        // id anyway so a mid-load click is a no-op rather than opening an adjust modal for an empty product.
        var productId = _product?.Id;
        if (string.IsNullOrEmpty(productId)) return;
        _dialogs.ShowModal(
            new StockAdjustModalViewModel(_service, _toast, _dialogs, productId, () =>
            {
                // Return to the product detail modal with refreshed on-hand, and refresh the list behind it.
                _dialogs.ShowModal(new ProductModalViewModel(_service, _toast, _dialogs, productId, _onChanged), 640);
                _onChanged?.Invoke();
            }),
            480);
    }
}

/// <summary>
/// One option of the product modal's category dropdown, mirroring the reference's
/// <c>&lt;option&gt;${c.label}&lt;/option&gt;</c> list (inventory.js renderInfoTab).
/// </summary>
public sealed class CategoryOptionViewModel
{
    public CategoryOptionViewModel(CategoryInfo category)
    {
        Id = category.Id;
        Label = category.Label;
    }

    public string Id { get; }
    public string Label { get; }
}
