using Animora.Contracts.Sales;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Sales;

/// <summary>
/// Backs the «عودت وجه فاکتور» modal (mirrors the reference's
/// <c>openRefundModal</c> / <c>.sw-modal.sw-confirm.sw-modal-refund</c>): the
/// amount input (with کل مبلغ / نیمی presets), the explanatory note and a
/// confirm-then-call flow through <see cref="ISalesQueryService.RefundAsync"/>.
/// </summary>
public sealed partial class RefundModalViewModel : ObservableObject
{
    private readonly ISalesQueryService _service;
    private readonly IToastService _toast;
    private readonly IDialogService _dialogs;
    private readonly string _invoiceId;
    private readonly long _maxAmount;
    private readonly Action? _onRefunded;

    public RefundModalViewModel(
        ISalesQueryService service,
        IToastService toast,
        IDialogService dialogs,
        string invoiceId,
        long maxAmount,
        Action? onRefunded = null)
    {
        _service = service;
        _toast = toast;
        _dialogs = dialogs;
        _invoiceId = invoiceId;
        _maxAmount = maxAmount;
        _onRefunded = onRefunded;

        AmountText = PersianNumberFormatter.ToPersianDigits(maxAmount);
        MaxText = SalePresentation.Money(maxAmount);
        HalfText = SalePresentation.Money(Math.Max(0, maxAmount / 2));
    }

    public string MaxText { get; }

    public string HalfText { get; }

    /// <summary>«کل مبلغ (…)» preset label.</summary>
    public string FullPresetText => $"کل مبلغ ({MaxText})";

    /// <summary>«نیمی (…)» preset label.</summary>
    public string HalfPresetText => $"نیمی ({HalfText})";

    [ObservableProperty]
    private string _amountText = string.Empty;

    [RelayCommand]
    private void SetFull() => AmountText = PersianNumberFormatter.ToPersianDigits(_maxAmount);

    [RelayCommand]
    private void SetHalf() => AmountText = PersianNumberFormatter.ToPersianDigits(Math.Max(0, _maxAmount / 2));

    [RelayCommand]
    private async Task SubmitAsync()
    {
        var amount = ParseLong(AmountText);
        var result = await _service.RefundAsync(_invoiceId, new RefundRequest(amount));
        if (!result.Ok)
        {
            _toast.Show(Animora.Desktop.Localization.ErrorMessages.ForResult(result.Error, "خطا در عودت"), Tone.Danger);
            return;
        }

        _dialogs.Close();
        _toast.Show($"مبلغ {SalePresentation.Money(result.Applied)} تومان عودت شد", Tone.Ok);
        _onRefunded?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => _dialogs.Close();

    private static long ParseLong(string text) =>
        long.TryParse(PersianNumberFormatter.ToAsciiDigits(text), out var v) ? v : 0;
}
