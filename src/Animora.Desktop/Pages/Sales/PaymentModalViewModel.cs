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
    private long _lastApplied;
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

        // Card is the clinic's most common tender → first chip and the default selection.
        Methods.Add(new PaymentMethodOptionViewModel(PaymentMethod.Card, true));
        Methods.Add(new PaymentMethodOptionViewModel(PaymentMethod.Cash));
        Methods.Add(new PaymentMethodOptionViewModel(PaymentMethod.Cheque));
        Methods.Add(new PaymentMethodOptionViewModel(PaymentMethod.Credit));
        Methods.Add(new PaymentMethodOptionViewModel(PaymentMethod.Mixed));

        _ = ModalLoadGuard.RunAsync(_toast, _dialogs, "بارگذاری فاکتور ناموفق بود؛ دوباره تلاش کنید", LoadAsync);
    }

    public ObservableCollection<PaymentMethodOptionViewModel> Methods { get; } = [];

    /// <summary>The «ترکیبی» split rows — each one is applied as a real payment with its own concrete method.</summary>
    public ObservableCollection<SplitPartViewModel> Parts { get; } = [];

    /// <summary>True while the «ترکیبی» chip is active (swaps the single amount row for the split editor).</summary>
    public bool IsMixed => SelectedMethod == PaymentMethod.Mixed;

    /// <summary>The single-amount editor (everything except «ترکیبی»).</summary>
    public bool IsSingle => !IsMixed;

    /// <summary>The «ترکیبی» split editor can drop a row only while more than one remains.</summary>
    public bool CanRemovePart => Parts.Count > 1;

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

    /// <summary>«کارت X + نقدی Y» — the split breakdown shown on the success screen for «ترکیبی».</summary>
    [ObservableProperty]
    private string _successParts = string.Empty;

    [ObservableProperty]
    private bool _hasSuccessParts;

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
        Methods.FirstOrDefault(m => m.IsActive)?.Method ?? PaymentMethod.Card;

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

        if (method == PaymentMethod.Mixed)
        {
            EnsureSeeded();
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
        OnPropertyChanged(nameof(IsMixed));
        OnPropertyChanged(nameof(IsSingle));
        OnPropertyChanged(nameof(CanRemovePart));

        if (IsMixed)
        {
            UpdateMixedPreview();
        }
        else
        {
            UpdateSinglePreview();
        }

        OnPropertyChanged(nameof(WillSettle));
        OnPropertyChanged(nameof(PayCtaLabel));
        PayCommand.NotifyCanExecuteChanged();
    }

    private void UpdateSinglePreview()
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
    }

    /// <summary>Live band for the split editor: Σ parts vs the amount still due.</summary>
    private void UpdateMixedPreview()
    {
        var sum = SumParts;
        WillSettle = sum > 0 && sum == _due && _due > 0;

        if (sum <= 0)
        {
            PreviewTone = "neutral";
            PreviewText = "مبلغ هر روش را وارد کنید.";
        }
        else if (sum > _due)
        {
            PreviewTone = "warn";
            PreviewText = "جمع اجزا بیش از مانده است؛ مقدار اضافه ثبت نمی‌شود — مبالغ را اصلاح کنید.";
        }
        else if (sum == _due)
        {
            PreviewTone = "success";
            PreviewText = "با این ترکیب فاکتور کاملاً تسویه می‌شود.";
        }
        else
        {
            PreviewTone = "partial";
            PreviewText = $"پرداخت ترکیبی جزئی؛ مانده پس از ثبت: {SalePresentation.Money(_due - sum)} تومان.";
        }
    }

    private long SumParts => Parts.Sum(p => p.Amount);

    /// <summary>Pay is blocked for an over-filled / empty split so a part is never silently capped.</summary>
    private bool CanPay() => !IsMixed || (SumParts > 0 && SumParts <= _due);

    [RelayCommand]
    private void AddPart()
    {
        var remaining = Math.Max(0, _due - SumParts);
        Parts.Add(NewPart(PaymentMethod.Cash, remaining));
        UpdatePreview();
    }

    private SplitPartViewModel NewPart(PaymentMethod method, long amount) =>
        new(method, amount, UpdatePreview, RemovePart);

    private void RemovePart(SplitPartViewModel part)
    {
        if (Parts.Count <= 1)
        {
            return;
        }

        Parts.Remove(part);
        UpdatePreview();
    }

    private void EnsureSeeded()
    {
        if (Parts.Count > 0)
        {
            return;
        }

        // Convenience seed: the whole due on card, an empty cash row ready to take a share.
        Parts.Add(NewPart(PaymentMethod.Card, _due));
        Parts.Add(NewPart(PaymentMethod.Cash, 0));
    }

    [RelayCommand(CanExecute = nameof(CanPay))]
    private async Task PayAsync()
    {
        if (IsMixed)
        {
            await PayMixedAsync();
            return;
        }

        var method = SelectedMethod;
        var amount = Amount == 0 ? _due : Amount;
        var result = await _service.TakePaymentAsync(
            _invoiceId, new PaymentRequest(method, amount, "پرداخت از صندوق"));

        if (!result.Ok)
        {
            _toast.Show(Animora.Desktop.Localization.ErrorMessages.ForResult(result.Error, "خطا در ثبت پرداخت"), Tone.Danger);
            return;
        }

        _onChanged?.Invoke();

        if (method == PaymentMethod.Cheque)
        {
            await MaybeRegisterChequeAsync(result.Invoice, result.Applied);
        }

        await FinishPaymentAsync(result, SalePresentation.MethodLabel(method), method.ToString().ToLowerInvariant(),
            result.Applied, null);
    }

    /// <summary>
    /// «ترکیبی»: applies every split row as its own real payment, sequentially (each one is a normal
    /// ledger row in History / owner file). The first failing part stops the run; parts already applied
    /// stay committed and are reported.
    /// </summary>
    private async Task PayMixedAsync()
    {
        var plan = Parts.Where(p => p.Amount > 0).Select(p => (p.Method, p.Amount)).ToList();
        PaymentResult? last = null;
        var applied = new List<(PaymentMethod Method, long Amount)>();
        string? failure = null;

        foreach (var (method, amount) in plan)
        {
            var result = await _service.TakePaymentAsync(
                _invoiceId, new PaymentRequest(method, amount, "پرداخت ترکیبی از صندوق"));
            if (!result.Ok)
            {
                failure = Animora.Desktop.Localization.ErrorMessages.ForResult(result.Error, "خطا در ثبت پرداخت");
                break;
            }

            last = result;
            applied.Add((method, result.Applied));
            if (method == PaymentMethod.Cheque)
            {
                await MaybeRegisterChequeAsync(result.Invoice, result.Applied);
            }
        }

        if (last is null)
        {
            _toast.Show(failure ?? "خطا در ثبت پرداخت", Tone.Danger);
            return;
        }

        _onChanged?.Invoke();
        var total = applied.Sum(a => a.Amount);
        var summary = string.Join(" + ", applied.Select(a =>
            $"{SalePresentation.MethodLabel(a.Method)} {SalePresentation.Money(a.Amount)}"));

        if (failure is not null)
        {
            _dialogs.Close();
            _toast.Show(
                $"{SalePresentation.Fa(applied.Count)} پرداخت ثبت شد ({summary}) اما بخش بعدی ناموفق بود: {failure} — مانده: {SalePresentation.Money(last.Invoice.Due)} تومان",
                Tone.Warn);
            return;
        }

        await FinishPaymentAsync(last, SalePresentation.MethodLabel(PaymentMethod.Mixed),
            PaymentMethod.Mixed.ToString().ToLowerInvariant(), total, summary);
    }

    /// <summary>Settled → celebration screen; otherwise close with the remaining-due toast.</summary>
    private async Task FinishPaymentAsync(PaymentResult last, string methodLabel, string methodTone, long applied, string? parts)
    {
        // A cheque prompt may have replaced this modal; bring it back before showing the outcome.
        _dialogs.ShowModal(this, 520);

        if (last.Settled)
        {
            await ShowSettlementSuccessAsync(last, methodLabel, methodTone, applied, parts);
        }
        else
        {
            _dialogs.Close();
            _toast.Show(
                $"پرداخت {SalePresentation.Money(applied)} تومان ثبت شد — مانده: {SalePresentation.Money(last.Invoice.Due)} تومان",
                Tone.Ok);
        }
    }

    /// <summary>
    /// Cheque tender hook (T5): replaced by the registration prompt. The payment itself is already
    /// recorded; this only decides whether a Cheques-module record is created alongside it.
    /// </summary>
    private Task MaybeRegisterChequeAsync(InvoiceDetail invoice, long amount) => Task.CompletedTask;

    private async Task ShowSettlementSuccessAsync(
        PaymentResult result, string methodLabel, string methodTone, long applied, string? parts)
    {
        var detail = result.Invoice;
        _lastResult = result;
        _lastApplied = applied;
        // Refund-from-success must offer what was actually collected, not the pre-payment paid value.
        _paid = detail.Paid;
        var readyCount = await GetReadyCountAsync();
        ReceiptNo = string.IsNullOrEmpty(detail.ReceiptNo) ? "—" : detail.ReceiptNo;
        SuccessCustomer = detail.IsGuest || detail.OwnerName.Length == 0
            ? "مشتری گذری"
            : detail.OwnerName + (detail.PetName.Length > 0 ? " · " + detail.PetName : string.Empty);
        SuccessMethodLabel = methodLabel;
        SuccessMethodTone = methodTone;
        SuccessParts = parts ?? string.Empty;
        HasSuccessParts = !string.IsNullOrEmpty(parts);
        SuccessThisPayment = SalePresentation.Money(applied);
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
            _lastResult.Invoice, _print.EmbeddedFontCss, "رسید پرداخت", _lastApplied);
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

/// <summary>One selectable tender in a split row (Card / Cash / Cheque / Credit — never «ترکیبی»).</summary>
public sealed record SplitMethodItem(PaymentMethod Method, string Label);

/// <summary>One row of the «ترکیبی» split editor: a concrete method + the amount taken with it.</summary>
public sealed partial class SplitPartViewModel : ObservableObject
{
    private static readonly SplitMethodItem[] Options =
    [
        new(PaymentMethod.Card, SalePresentation.MethodLabel(PaymentMethod.Card)),
        new(PaymentMethod.Cash, SalePresentation.MethodLabel(PaymentMethod.Cash)),
        new(PaymentMethod.Cheque, SalePresentation.MethodLabel(PaymentMethod.Cheque)),
        new(PaymentMethod.Credit, SalePresentation.MethodLabel(PaymentMethod.Credit)),
    ];

    private readonly Action _changed;
    private readonly Action<SplitPartViewModel> _remove;

    public SplitPartViewModel(PaymentMethod method, long amount, Action changed, Action<SplitPartViewModel> remove)
    {
        _changed = changed;
        _remove = remove;
        _selectedOption = Options.First(o => o.Method == method);
        _amountText = PersianNumberFormatter.ToPersianDigits(amount);
    }

    public IReadOnlyList<SplitMethodItem> MethodOptions => Options;

    [ObservableProperty]
    private SplitMethodItem _selectedOption;

    [ObservableProperty]
    private string _amountText;

    public PaymentMethod Method => SelectedOption.Method;

    public long Amount
    {
        get
        {
            var ascii = PersianNumberFormatter.ToAsciiDigits(AmountText ?? string.Empty);
            var digits = new string(ascii.Where(char.IsDigit).ToArray());
            return long.TryParse(digits, out var v) ? Math.Max(0, v) : 0;
        }
    }

    partial void OnSelectedOptionChanged(SplitMethodItem value) => _changed();

    partial void OnAmountTextChanged(string value) => _changed();

    [RelayCommand]
    private void Remove() => _remove(this);
}
