using Animora.Contracts.Sales;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Sales;

/// <summary>
/// Backs the «افزودن آیتم دلخواه» modal (mirrors the reference's
/// <c>openCustomItemModal</c> / <c>.sw-modal.sw-confirm.sw-modal-custom</c>):
/// the name / unit-price / quantity form with its live row-total preview and a
/// submit-then-call flow through
/// <see cref="ISalesQueryService.AddCustomLineAsync"/>.
/// </summary>
public sealed partial class CustomItemModalViewModel : ObservableObject
{
    private readonly ISalesQueryService _service;
    private readonly IToastService _toast;
    private readonly IDialogService _dialogs;
    private readonly string _invoiceId;
    private readonly Action? _onAdded;
    private readonly bool _isLocked;

    public CustomItemModalViewModel(
        ISalesQueryService service,
        IToastService toast,
        IDialogService dialogs,
        string invoiceId,
        bool isLocked = false,
        Action? onAdded = null)
    {
        _service = service;
        _toast = toast;
        _dialogs = dialogs;
        _invoiceId = invoiceId;
        _isLocked = isLocked;
        _onAdded = onAdded;
    }

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _priceText = string.Empty;

    [ObservableProperty]
    private string _quantityText = "۱";

    [ObservableProperty]
    private string _previewText = string.Empty;

    [ObservableProperty]
    private bool _hasPreview;

    partial void OnPriceTextChanged(string value) => UpdatePreview();

    partial void OnQuantityTextChanged(string value) => UpdatePreview();

    private void UpdatePreview()
    {
        var price = Math.Max(0, ParseLong(PriceText));
        var qty = Math.Clamp(ParseLong(QuantityText), 1, 999);
        if (price > 0)
        {
            PreviewText = $"جمع این ردیف: {SalePresentation.Money(price * qty)} تومان";
            HasPreview = true;
        }
        else
        {
            PreviewText = string.Empty;
            HasPreview = false;
        }
    }

    [RelayCommand]
    private async Task SubmitAsync()
    {
        var title = Name.Trim();
        if (title.Length == 0)
        {
            _toast.Show("نام آیتم را وارد کنید", Tone.Danger);
            return;
        }

        var price = Math.Max(0, ParseLong(PriceText));
        if (price <= 0)
        {
            _toast.Show("مبلغ باید بزرگ‌تر از صفر باشد", Tone.Danger);
            return;
        }

        var qty = (int)Math.Clamp(ParseLong(QuantityText), 1, 999);
        await _service.AddCustomLineAsync(_invoiceId, new CustomLineRequest(title, price, qty));
        _dialogs.Close();
        _toast.Show($"«{title}» به فاکتور افزوده شد", Tone.Ok);
        _onAdded?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => _dialogs.Close();

    private static long ParseLong(string text) =>
        long.TryParse(PersianNumberFormatter.ToAsciiDigits(text), out var v) ? v : 0;
}
