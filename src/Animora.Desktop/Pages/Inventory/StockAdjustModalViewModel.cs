using System.Collections.ObjectModel;
using Animora.Contracts.Inventory;
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
        _ = LoadAsync(productId);
    }

    [ObservableProperty] private string _categoryLabel = string.Empty;
    [ObservableProperty] private string _code = string.Empty;
    [ObservableProperty] private string _productName = string.Empty;
    [ObservableProperty] private string _glyph = "📦";
    [ObservableProperty] private IBrush _thumbBrush = InventoryPresentation.ThumbBrush("a");

    /// <summary>The header eyebrow: «category · code» (reference <c>${cat.label} · ${p.code}</c>).</summary>
    public string EyebrowLine => $"{CategoryLabel} · {Code}";

    partial void OnCategoryLabelChanged(string value) => OnPropertyChanged(nameof(EyebrowLine));
    partial void OnCodeChanged(string value) => OnPropertyChanged(nameof(EyebrowLine));

    public ObservableCollection<InventoryAdjustRowViewModel> Rows { get; } = [];

    private async Task LoadAsync(string productId)
    {
        var detail = await _service.GetDetailAsync(productId);
        if (detail is null)
        {
            _dialogs.Close();
            return;
        }

        _product = detail.Product;
        Glyph = _product.Glyph;
        ThumbBrush = InventoryPresentation.ThumbBrush(_product.Tone);
        Code = _product.Code;
        ProductName = _product.Name;

        CategoryLabel = (await _service.GetCategoriesAsync())
            .FirstOrDefault(c => c.Id == _product.CategoryId)?.Label ?? string.Empty;

        foreach (var variant in _product.Variants)
        {
            Rows.Add(new InventoryAdjustRowViewModel(variant));
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        foreach (var row in Rows)
        {
            var stock = row.ReadStock();
            if (stock != row.OriginalStock)
            {
                await _service.AdjustStockAsync(_product.Id, row.Id, new StockAdjustRequest(stock));
            }
        }

        _dialogs.Close();
        _toast.Show("موجودی به‌روزرسانی شد (نمونه طراحی)");
        _onChanged?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => _dialogs.Close();
}
