using System.Collections.ObjectModel;
using Animora.Contracts.Sales;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Services;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Sales;

/// <summary>
/// Backs the payment surface + settlement-success flow (mirrors the reference's
/// <c>renderPayment</c> + <c>doPayment</c> + <c>showSettlementSuccess</c>): the
/// five-method grid (<c>.sw-methods</c>), the amount input with its
/// مانده کامل / ٪۵۰ / صفر presets (<c>.sw-pay-presets</c>), the live preview
/// band (<c>.sw-pay-preview</c> neutral/success/warn/partial) and — once the
/// invoice settles — the celebration screen (<c>.sw-success</c>). Payment goes
/// through <see cref="ISalesQueryService.TakePaymentAsync"/>.
/// </summary>
public sealed partial class PaymentModalViewModel : ObservableObject
{
    private readonly ISalesQueryService _service;
    private readonly IToastService _toast;
    private readonly IDialogService _dialogs;
    private readonly IPrintService _print;
    private readonly string _invoiceId;
    private readonly Action? _onChanged;

    private long _due;
    private long _total;
    private long _paid;
    private PaymentResult? _lastResult;
    private int _lastReadyCount;

    public PaymentModalViewModel(
        ISalesQueryService service,
        IToastService toast,
        IDialogService dialogs,
        IPrintService print,
        string invoiceId,
        Action? onChanged = null)
    {
        _service = service;
        _toast = toast;
        _dialogs = dialogs;
        _print = print;
        _invoiceId = invoiceId;
        _onChanged = onChanged;

        Methods.Add(new PaymentMethodOptionViewModel(PaymentMethod.Cash, true));
        Methods.Add(new PaymentMethodOptionViewModel(PaymentMethod.Card));
        Methods.Add(new PaymentMethodOptionViewModel(PaymentMethod.Cheque));
        Methods.Add(new PaymentMethodOptionViewModel(PaymentMethod.Credit));
        Methods.Add(new PaymentMethodOptionViewModel(PaymentMethod.Mixed));

        _ = ModalLoadGuard.RunAsync(_toast, _dialogs, "بارگذاری فاکتور ناموفق بود؛ دوباره تلاش کنید", LoadAsync);
    }

    public ObservableCollection<PaymentMethodOptionViewModel> Methods { get; } = [];

    [ObservableProperty]
    private string _amountText = "۰";

    [ObservableProperty]
    private string _dueText = "۰";

    [ObservableProperty]
    private string _previewText = string.Empty;

    [ObservableProperty]
    private string _previewTone = "neutral";

    public IBrush PreviewBackground => SalePresentation.PreviewBrushes(PreviewTone).Background;

    public IBrush PreviewForeground => SalePresentation.PreviewBrushes(PreviewTone).Foreground;

    public IBrush PreviewIcoBackground => SalePresentation.PreviewBrushes(PreviewTone).IcoBackground;

    public IBrush PreviewIcoForeground => SalePresentation.PreviewBrushes(PreviewTone).IcoForeground;

    public string PreviewIconKey => SalePresentation.PreviewIconKey(PreviewTone);

    partial void OnPreviewToneChanged(string value)
    {
        OnPropertyChanged(nameof(PreviewBackground));
        OnPropertyChanged(nameof(PreviewForeground));
        OnPropertyChanged(nameof(PreviewIcoBackground));
        OnPropertyChanged(nameof(PreviewIcoForeground));
        OnPropertyChanged(nameof(PreviewIconKey));
    }

    [ObservableProperty]
    private bool _showSuccess;

    // ── success-screen fields (showSettlementSuccess) ──
    [ObservableProperty]
    private string _receiptNo = string.Empty;

    [ObservableProperty]
    private string _successCustomer = string.Empty;

    [ObservableProperty]
    private string _successMethodLabel = string.Empty;

    [ObservableProperty]
    private string _successMethodTone = string.Empty;

    [ObservableProperty]
    private string _successThisPayment = string.Empty;

    [ObservableProperty]
    private string _successTotal = string.Empty;

    [ObservableProperty]
    private string _successNextLabel = string.Empty;

    /// <summary>The CTA label (renderPayCTA): full settlement vs. partial.</summary>
    public string PayCtaLabel => WillSettle ? "پرداخت و تسویه کامل" : "ثبت پرداخت جزئی";

    public bool WillSettle { get; private set; }

    private PaymentMethod SelectedMethod =>
        Methods.FirstOrDefault(m => m.IsActive)?.Method ?? PaymentMethod.Cash;

    private async Task LoadAsync()
    {
        var detail = await _service.GetInvoiceAsync(_invoiceId);
        if (detail is null)
        {
            _toast.Show("فاکتور موردنظر یافت نشد", Tone.Warn);
            _dialogs.Close();
            return;
        }

        _due = detail.Due;
        _total = detail.Total;
        _paid = detail.Paid;
        DueText = SalePresentation.Money(_due);
        AmountText = PersianNumberFormatter.ToPersianDigits(_due);
        UpdatePreview();
    }

    private long Amount => long.TryParse(PersianNumberFormatter.ToAsciiDigits(AmountText), out var v) ? Math.Max(0, v) : 0;

    [RelayCommand]
    private void ChooseMethod(string? key)
    {
        if (!Enum.TryParse<PaymentMethod>(key, out var method))
        {
            return;
        }

        foreach (var m in Methods)
        {
            m.IsActive = m.Method == method;
        }

        UpdatePreview();
    }

    [RelayCommand]
    private void SetDue()
    {
        AmountText = PersianNumberFormatter.ToPersianDigits(_due);
    }

    [RelayCommand]
    private void SetHalf()
    {
        AmountText = PersianNumberFormatter.ToPersianDigits(Math.Max(0, _due / 2));
    }

    [RelayCommand]
    private void SetClear() => AmountText = "۰";

    partial void OnAmountTextChanged(string value) => UpdatePreview();

    private void UpdatePreview()
    {
        var amt = Amount == 0 ? _due : Amount;
        var capped = Math.Min(amt, _due);
        WillSettle = capped >= _due && _due > 0;
        var afterPay = Math.Max(0, _due - capped);
        var over = Amount > _due;

        if (Amount <= 0)
        {
            PreviewTone = "neutral";
            PreviewText = "مبلغ پرداخت را وارد کنید یا یکی از میان‌برها را انتخاب نمایید.";
        }
        else if (WillSettle)
        {
            PreviewTone = "success";
            PreviewText = "با این پرداخت فاکتور کاملاً تسویه می‌شود.";
        }
        else if (over)
        {
            PreviewTone = "warn";
            PreviewText = $"مبلغ بیش از مانده است — فقط {SalePresentation.Money(_due)} تومان ثبت خواهد شد.";
        }
        else
        {
            PreviewTone = "partial";
            PreviewText = $"پرداخت جزئی؛ پس از این ثبت {SalePresentation.Money(afterPay)} تومان مانده باقی می‌ماند.";
        }

        OnPropertyChanged(nameof(WillSettle));
        OnPropertyChanged(nameof(PayCtaLabel));
    }

    [RelayCommand]
    private async Task PayAsync()
    {
        var amount = Amount == 0 ? _due : Amount;
        var result = await _service.TakePaymentAsync(
            _invoiceId, new PaymentRequest(SelectedMethod, amount, "پرداخت از صندوق"));

        if (!result.Ok)
        {
            _toast.Show(Animora.Desktop.Localization.ErrorMessages.ForResult(result.Error, "خطا در ثبت پرداخت"), Tone.Danger);
            return;
        }

        _onChanged?.Invoke();

        if (result.Settled)
        {
            await ShowSettlementSuccessAsync(result);
        }
        else
        {
            _dialogs.Close();
            _toast.Show(
                $"پرداخت {SalePresentation.Money(result.Applied)} تومان ثبت شد — مانده: {SalePresentation.Money(result.Invoice.Due)} تومان",
                Tone.Ok);
        }
    }

    private async Task ShowSettlementSuccessAsync(PaymentResult result)
    {
        var detail = result.Invoice;
        _lastResult = result;
        // Refund-from-success must offer what was actually collected, not the pre-payment paid value.
        _paid = detail.Paid;
        var readyCount = await GetReadyCountAsync();
        ReceiptNo = string.IsNullOrEmpty(detail.ReceiptNo) ? "—" : detail.ReceiptNo;
        SuccessCustomer = detail.IsGuest || detail.OwnerName.Length == 0
            ? "مشتری گذری"
            : detail.OwnerName + (detail.PetName.Length > 0 ? " · " + detail.PetName : string.Empty);
        SuccessMethodLabel = SalePresentation.MethodLabel(SelectedMethod);
        SuccessMethodTone = SelectedMethod.ToString().ToLowerInvariant();
        SuccessThisPayment = SalePresentation.Money(result.Applied);
        SuccessTotal = SalePresentation.Money(detail.Paid);
        SuccessNextLabel = readyCount > 0
            ? $"ویزیت بعدی ({SalePresentation.Fa(readyCount)})"
            : "فاکتور جدید";

        ShowSuccess = true;
    }

    /// <summary>
    /// Reads the ready-visit queue size. Only typed service failures are caught (X40): the
    /// failure is traced and surfaced as a warning, and the last known count is kept instead
    /// of masquerading as an empty queue.
    /// </summary>
    private async Task<int> GetReadyCountAsync()
    {
        try
        {
            var list = await _service.GetReadyEncountersAsync();
            _lastReadyCount = list.Count;
        }
        catch (InvalidOperationException ex)
        {
            System.Diagnostics.Trace.TraceWarning("Ready-visit count failed: " + ex.Message);
            _toast.Show("شمارش ویزیت‌های آمادهٔ صدور فاکتور ممکن نشد", Tone.Warn);
        }

        return _lastReadyCount;
    }

    [RelayCommand]
    private void RefundFromSuccess()
    {
        _dialogs.Close();
        _dialogs.ShowModal(new RefundModalViewModel(_service, _toast, _dialogs, _invoiceId, _paid, _onChanged), 480);
    }

    /// <summary>Opens the payment receipt (the settled invoice + this payment) in the print preview (X13).</summary>
    [RelayCommand]
    private async Task Print()
    {
        if (_lastResult is null)
        {
            return;
        }

        var html = SalesPrintDocuments.Invoice(
            _lastResult.Invoice, _print.EmbeddedFontCss, "رسید پرداخت", _lastResult.Applied);
        await _print.OpenWithToastAsync(_toast, $"receipt-{_lastResult.Invoice.Id}", html);
    }

    [RelayCommand]
    private void Close() => _dialogs.Close();

    [RelayCommand]
    private void Next()
    {
        _dialogs.Close();
        _onChanged?.Invoke();
    }
}

/// <summary>One method chip of the five-method grid (mirrors <c>.sw-method</c>).</summary>
public sealed partial class PaymentMethodOptionViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isActive;

    public PaymentMethodOptionViewModel(PaymentMethod method, bool isActive = false)
    {
        Method = method;
        IsActive = isActive;
        Key = method.ToString();
        Label = SalePresentation.MethodLabel(method);
        IconKey = SalePresentation.MethodIconKey(method);
    }

    public PaymentMethod Method { get; }

    public string Key { get; }

    public string Label { get; }

    public string IconKey { get; }
}
