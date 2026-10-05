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
/// Backs the Inventory «کالای جدید» modal (Animora-UI/assets/inventory.js
/// <c>openNewProductModal</c>): the base-info fields, the تک‌واحد / چند‌تنوعی
/// type toggle, the single-unit price/stock block (default) and the opt-in
/// variant setup. Submits through <see cref="IInventoryQueryService.CreateProductAsync"/>
/// (R-18).
/// </summary>
public sealed partial class NewProductModalViewModel : ObservableObject
{
    private readonly IInventoryQueryService _service;
    private readonly IToastService _toast;
    private readonly IDialogService _dialogs;
    private readonly Action? _onCreated;

    public NewProductModalViewModel(
        IInventoryQueryService service,
        IToastService toast,
        IDialogService dialogs,
        Action? onCreated = null)
    {
        _service = service;
        _toast = toast;
        _dialogs = dialogs;
        _onCreated = onCreated;

        TypeOptions.Add(new InventoryProductTypeOptionViewModel(
            "no", "IconBox", "تک‌واحد", "یک قیمت و موجودی واحد") { IsActive = true });
        TypeOptions.Add(new InventoryProductTypeOptionViewModel(
            "yes", "IconLayers", "چند‌تنوعی", "وزن، سایز یا دوز مختلف"));
        TypeOptions.Add(new InventoryProductTypeOptionViewModel(
            "svc", "IconClipboard", "خدمت", "بدون موجودی، فقط نرخ"));

        _ = LoadSafeAsync();
    }

    // ── base info ───────────────────────────────────────────────────────────
    /// <summary>The header thumbnail gradient — reference's brand-400→brand-600 fill.</summary>
    public IBrush ThumbBrush { get; } = InventoryPresentation.NewProductThumbBrush();

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _codeText = string.Empty;
    [ObservableProperty] private string _brand = string.Empty;
    [ObservableProperty] private string _description = string.Empty;

    public ObservableCollection<CategoryOptionViewModel> CategoryOptions { get; } = [];
    [ObservableProperty] private CategoryOptionViewModel? _selectedCategory;

    // ── type toggle ─────────────────────────────────────────────────────────
    public ObservableCollection<InventoryProductTypeOptionViewModel> TypeOptions { get; } = [];

    [ObservableProperty] private bool _hasVariants;

    /// <summary>A stock-free service: only a name, category and rate are needed (stored as <c>ProductKind.Service</c>).</summary>
    [ObservableProperty] private bool _isService;

    public bool ShowSingleFields => !HasVariants && !IsService;
    public bool ShowVariantFields => HasVariants && !IsService;
    public bool ShowServiceFields => IsService;
    public bool ShowCodeAndBrand => !IsService;

    partial void OnHasVariantsChanged(bool value) => NotifyTypeChanged();

    partial void OnIsServiceChanged(bool value) => NotifyTypeChanged();

    private void NotifyTypeChanged()
    {
        OnPropertyChanged(nameof(ShowSingleFields));
        OnPropertyChanged(nameof(ShowVariantFields));
        OnPropertyChanged(nameof(ShowServiceFields));
        OnPropertyChanged(nameof(ShowCodeAndBrand));
    }

    [RelayCommand]
    private void ChooseType(string? key)
    {
        HasVariants = key == "yes";
        IsService = key == "svc";
        foreach (var option in TypeOptions)
        {
            option.IsActive = option.Key == key;
        }
    }

    // ── single-unit fields ──────────────────────────────────────────────────
    [ObservableProperty] private string _stockText = string.Empty;
    [ObservableProperty] private string _reorderText = string.Empty;
    [ObservableProperty] private string _costText = string.Empty;
    [ObservableProperty] private string _priceText = string.Empty;

    // ── variant setup ───────────────────────────────────────────────────────
    [ObservableProperty] private string _attributeName = string.Empty;
    [ObservableProperty] private string _attributeOptionsText = string.Empty;

    /// <summary>Observed initial load (P2-9): a seam failure toasts and closes the modal instead of escaping the ctor task.</summary>
    private async Task LoadSafeAsync()
    {
        try
        {
            await LoadAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _toast.Show(
                ex is InvalidOperationException ioe
                    ? InventoryPresentation.MutationFailureMessage(ioe, "بارگذاری دسته‌ها ناموفق بود؛ دوباره تلاش کنید")
                    : "بارگذاری دسته‌ها ناموفق بود؛ دوباره تلاش کنید",
                Tone.Danger);
            _dialogs.Close();
        }
    }

    private async Task LoadAsync()
    {
        foreach (var category in await _service.GetCategoriesAsync())
        {
            CategoryOptions.Add(new CategoryOptionViewModel(category));
        }

        SelectedCategory = CategoryOptions.FirstOrDefault();
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        var name = Name.Trim();
        if (name.Length == 0)
        {
            _toast.Show("نام کالا را وارد کنید", Tone.Danger);
            return;
        }

        var code = CodeText.Trim();
        var brand = Brand.Trim();
        var categoryId = SelectedCategory?.Id ?? CategoryOptions.FirstOrDefault()?.Id ?? "med";

        IReadOnlyList<string> attributeOptions = [];
        IReadOnlyList<ProductVariant> variants;

        if (IsService)
        {
            // A service has no stock/variants; the rate is the only commercial field.
            variants = [];
        }
        else if (HasVariants)
        {
            var attrName = AttributeName.Trim();
            attributeOptions = AttributeOptionsText
                .Split([',', '،'], StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToList();
            if (attributeOptions.Count == 0)
            {
                attributeOptions = ["گزینه ۱"];
            }

            variants = [];
        }
        else
        {
            var stock = ParseInt(StockText);
            var reorder = ParseInt(ReorderText);
            variants =
            [
                new ProductVariant(
                    "v_" + Guid.NewGuid().ToString("N")[..6],
                    "تک واحد",
                    code.Length > 0 ? code : "—",
                    stock,
                    reorder == 0 ? 5 : reorder,
                    ParseLong(CostText),
                    ParseLong(PriceText),
                    null),
            ];
        }

        var request = new NewProductRequest(
            code, name, brand, categoryId, Description.Trim(),
            HasVariants ? AttributeName.Trim() : null,
            attributeOptions,
            variants,
            IsService,
            ParseLong(CostText),
            ParseLong(PriceText));

        try
        {
            await _service.CreateProductAsync(request);
        }
        catch (InvalidOperationException ex)
        {
            // Keep the modal open so the user can correct the form; never claim success.
            _toast.Show(InventoryPresentation.MutationFailureMessage(ex, "ساخت کالا ناموفق بود؛ دوباره تلاش کنید"), Tone.Danger);
            return;
        }

        _dialogs.Close();
        _toast.Show(IsService ? "خدمت ساخته شد" : "کالا ساخته شد", Tone.Ok);
        _onCreated?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => _dialogs.Close();

    private static int ParseInt(string text) =>
        int.TryParse(PersianNumberFormatter.ToAsciiDigits(text), out var v) ? v : 0;

    private static long ParseLong(string text) =>
        long.TryParse(PersianNumberFormatter.ToAsciiDigits(text), out var v) ? v : 0;
}
