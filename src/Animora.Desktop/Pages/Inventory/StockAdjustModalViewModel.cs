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
/// Backs the Inventory «تغییر موجودی» (stock-adjust) modal
/// (Animora-UI/assets/inventory.js <c>openStockAdjustModal</c>/
/// <c>renderStockAdjustModal</c>): the toned header (category · code eyebrow, the
/// product name under the «تغییر موجودی» title) and one adjust row per variant
/// (label, SKU · reorder point and a stock stepper). «ثبت موجودی» writes each row
/// back and logs the delta as a history entry via
/// <see cref="IInventoryQueryService.AdjustStockAsync"/> (R-18).
/// </summary>
public sealed partial class StockAdjustModalViewModel : ObservableObject
{
    private readonly IInventoryQueryService _service;
    private readonly IToastService _toast;
    private readonly IDialogService _dialogs;
    private readonly Action? _onChanged;

    private Product _product = null!;

    public StockAdjustModalViewModel(
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
        _selectedReason = ReasonOptions[0];
        _ = LoadSafeAsync(productId);
    }

    [ObservableProperty] private string _categoryLabel = string.Empty;
    [ObservableProperty] private string _code = string.Empty;
    [ObservableProperty] private string _productName = string.Empty;

    /// <summary>The header eyebrow: «category · code» (reference <c>${cat.label} · ${p.code}</c>).</summary>
    public string EyebrowLine => $"{CategoryLabel} · {Code}";

    partial void OnCategoryLabelChanged(string value) => OnPropertyChanged(nameof(EyebrowLine));
    partial void OnCodeChanged(string value) => OnPropertyChanged(nameof(EyebrowLine));

    public ObservableCollection<InventoryAdjustRowViewModel> Rows { get; } = [];

    /// <summary>Observed initial load (P2-9): a seam failure toasts and closes the modal instead of escaping the ctor task.</summary>
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
                    ? InventoryPresentation.MutationFailureMessage(ioe, "بارگذاری موجودی کالا ناموفق بود؛ دوباره تلاش کنید")
                    : "بارگذاری موجودی کالا ناموفق بود؛ دوباره تلاش کنید",
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
        Code = _product.Code;
        ProductName = _product.Name;

        CategoryLabel = (await _service.GetCategoriesAsync())
            .FirstOrDefault(c => c.Id == _product.CategoryId)?.Label ?? string.Empty;

        foreach (var variant in _product.Variants)
        {
            Rows.Add(new InventoryAdjustRowViewModel(variant));
        }
    }

    /// <summary>The selectable reasons (Count/Damage/Expiry/Theft/Correction); <c>Correction</c> is the default.</summary>
    public IReadOnlyList<AdjustReasonOption> ReasonOptions { get; } =
    [
        new(StockAdjustReasons.Correction, "اصلاح موجودی"),
        new(StockAdjustReasons.Count, "شمارش انبار"),
        new(StockAdjustReasons.Damage, "خرابی / شکستگی"),
        new(StockAdjustReasons.Expiry, "انقضا"),
        new(StockAdjustReasons.Theft, "مفقودی / سرقت"),
    ];

    [ObservableProperty] private AdjustReasonOption? _selectedReason;

    [RelayCommand]
    private async Task SaveAsync()
    {
        var lines = new List<StockAdjustLine>(Rows.Count);
        foreach (var row in Rows)
        {
            var stock = row.ReadStock();
            if (stock != row.OriginalStock)
            {
                lines.Add(new StockAdjustLine(row.Id, stock));
            }
        }

        if (lines.Count == 0)
        {
            _dialogs.Close();
            return;
        }

        var reason = SelectedReason?.Code ?? StockAdjustReasons.Correction;
        try
        {
            // One atomic command for the whole product, the reason recorded on every movement.
            await _service.AdjustStockAsync(_product.Id, new StockAdjustRequest(lines, reason));
        }
        catch (InvalidOperationException ex)
        {
            _toast.Show(InventoryPresentation.MutationFailureMessage(ex, "به‌روزرسانی موجودی ناموفق بود؛ دوباره تلاش کنید"), Tone.Danger);
            return;
        }

        _dialogs.Close();
        _toast.Show("موجودی به‌روزرسانی شد", Tone.Ok);
        _onChanged?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => _dialogs.Close();
}

/// <summary>One selectable reason in the stock-adjust modal.</summary>
public sealed record AdjustReasonOption(string Code, string Label);
