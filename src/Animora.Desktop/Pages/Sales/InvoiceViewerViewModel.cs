using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Animora.Contracts.Sales;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Services;
using Animora.Desktop.Pages.Sales.Shared;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Sales;

/// <summary>
/// Backs the center-screen «مشاهدهٔ فاکتور» viewer: it opens an invoice over the
/// page (via <see cref="IDialogService.ShowSheet"/>), shows its full detail —
/// header, lines, totals, payment history and the «تاریخچه تغییرات» audit trail —
/// and offers the edit / delete / void / refund / pay actions. Every mutation is
/// routed through <see cref="ISalesQueryService"/> so the Sales service records the change
/// (with its full detail) in the audit trail and the cashier ledger, then the
/// viewer reloads and notifies the caller so the page/ledger refresh too.
/// </summary>
public sealed partial class InvoiceViewerViewModel : ObservableObject
{
    private readonly ISalesQueryService _service;
    private readonly IToastService _toast;
    private readonly IDialogService _dialogs;
    private readonly IPrintService _print;
    private readonly string _invoiceId;
    private readonly Action? _onChanged;

    private InvoiceDetail _detail = null!;

    public InvoiceViewerViewModel(
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

        _ = ModalLoadGuard.RunAsync(_toast, _dialogs, "بارگذاری فاکتور ناموفق بود؛ دوباره تلاش کنید", LoadAsync);
    }

    public RangeObservableCollection<InvoiceViewerLineViewModel> Lines { get; } = [];

    public RangeObservableCollection<InvoicePaymentViewModel> Payments { get; } = [];

    public RangeObservableCollection<InvoiceAuditRowViewModel> Audit { get; } = [];

    [ObservableProperty]
    private string _invoiceId2 = string.Empty;

    [ObservableProperty]
    private string _title = "فاکتور";

    [ObservableProperty]
    private string _subtitle = string.Empty;

    [ObservableProperty]
    private string _stateLabel = string.Empty;

    [ObservableProperty]
    private Tone _stateTone = Tone.Neutral;

    [ObservableProperty]
    private string _sourceLabel = string.Empty;

    [ObservableProperty]
    private string _dateText = string.Empty;

    // Header meta strip (time / customer / context).
    [ObservableProperty]
    private string _ownerName = string.Empty;

    [ObservableProperty]
    private string _phoneText = string.Empty;

    [ObservableProperty]
    private string _timeText = "—";

    [ObservableProperty]
    private bool _hasTime;

    [ObservableProperty]
    private string _doctorText = string.Empty;

    [ObservableProperty]
    private bool _hasDoctor;

    [ObservableProperty]
    private string _insuranceLabel = string.Empty;

    [ObservableProperty]
    private bool _hasInsuranceLabel;

    [ObservableProperty]
    private string _itemCountText = "۰";

    // Totals.
    [ObservableProperty]
    private string _baseText = "۰";

    [ObservableProperty]
    private string _discountText = "۰";

    [ObservableProperty]
    private string _taxText = "۰";

    [ObservableProperty]
    private string _insuranceShareText = "۰";

    [ObservableProperty]
    private string _totalText = "۰";

    [ObservableProperty]
    private string _paidText = "۰";

    [ObservableProperty]
    private string _dueText = "۰";

    [ObservableProperty]
    private bool _hasInsurance;

    [ObservableProperty]
    private bool _hasDiscount;

    [ObservableProperty]
    private bool _hasDue;

    [ObservableProperty]
    private bool _hasPayments;

    [ObservableProperty]
    private bool _hasAudit;

    [ObservableProperty]
    private string _paymentsHeader = "تاریخچهٔ پرداخت‌ها";

    [ObservableProperty]
    private string _auditHeader = "تاریخچه تغییرات";

    // Action visibility.
    [ObservableProperty]
    private bool _canEdit;

    [ObservableProperty]
    private bool _showVoid;

    [ObservableProperty]
    private bool _showRefund;

    [ObservableProperty]
    private bool _canPay;

    [ObservableProperty]
    private bool _isDeleted;

    [ObservableProperty]
    private bool _showReturn;

    [ObservableProperty]
    private bool _canEditMeta;

    [ObservableProperty]
    private string _receiptText = string.Empty;

    [ObservableProperty]
    private bool _hasReceipt;

    // ── Discount / insurance editors (draft/partial only) ──────────────────

    public ObservableCollection<InsurerOptionViewModel> Insurers { get; } = [];

    [ObservableProperty]
    private int _discountKindIndex;

    [ObservableProperty]
    private string _discountValueText = "۰";

    [ObservableProperty]
    private InsurerOptionViewModel? _selectedInsurer;

    private bool _insurersLoaded;
    private bool _suppressInsuranceApply;

    partial void OnSelectedInsurerChanged(InsurerOptionViewModel? value)
    {
        if (_suppressInsuranceApply)
        {
            return;
        }

        // Remember the value the user just picked, and roll the selection back to
        // the currently-applied insurer so the change only "sticks" after they
        // confirm. If they cancel, the ComboBox stays on the applied option.
        var appliedId = _detail?.InsuranceOrgId ?? string.Empty;
        var chosen = value;
        var chosenId = chosen?.Id ?? string.Empty;
        var chosenLabel = string.IsNullOrEmpty(chosenId) ? "بدون بیمه" : chosen?.Label ?? "بیمه";

        _suppressInsuranceApply = true;
        SelectedInsurer = Insurers.FirstOrDefault(o => o.Id == appliedId) ?? Insurers.FirstOrDefault();
        _suppressInsuranceApply = false;

        _dialogs.ShowModal(new ConfirmDialogViewModel(
            _dialogs,
            "brand",
            "IconShield",
            "تغییر پوشش بیمه",
            "پوشش بیمهٔ این فاکتور به ",
            $"«{chosenLabel}»",
            " تغییر کند؟",
            "سهم بیمه و مبلغ نهایی فاکتور بازمحاسبه و در تاریخچه تغییرات ثبت می‌شود.",
            "بله، اعمال کن",
            "انصراف",
            () => _ = ApplyInsuranceAsync(chosenId)), 480);
    }

    public string DeletedBannerText => "این فاکتور حذف شده است — رکورد آن برای پیگیری در تاریخچه باقی می‌ماند.";

    private async Task LoadAsync()
    {
        var detail = await _service.GetInvoiceAsync(_invoiceId);
        if (detail is null)
        {
            _toast.Show("فاکتور موردنظر یافت نشد", Tone.Warn);
            _dialogs.Close();
            return;
        }

        await EnsureInsurersLoadedAsync();
        Apply(detail);
    }

    private async Task EnsureInsurersLoadedAsync()
    {
        if (_insurersLoaded)
        {
            return;
        }

        _insurersLoaded = true;
        _suppressInsuranceApply = true;
        Insurers.Clear();
        Insurers.Add(new InsurerOptionViewModel(string.Empty, "بدون بیمه"));
        foreach (var insurer in await _service.GetInsurersAsync())
        {
            Insurers.Add(new InsurerOptionViewModel(insurer.Id, insurer.Name));
        }

        _suppressInsuranceApply = false;
    }

    private void Apply(InvoiceDetail detail)
    {
        _detail = detail;
        InvoiceId2 = detail.Id;
        Title = detail.PetName.Length > 0 ? detail.PetName : "فاکتور";
        Subtitle = $"{detail.OwnerName} · {detail.Phone}";
        StateLabel = detail.StateLabel;
        StateTone = SalePresentation.StateTone(detail.State);
        SourceLabel = detail.SourceLabel;
        DateText = detail.HasVisitContext ? detail.VisitCompletedAt : string.Empty;

        OwnerName = detail.IsGuest ? "مشتری گذری" : detail.OwnerName;
        PhoneText = string.IsNullOrWhiteSpace(detail.Phone) ? "بدون شماره" : detail.Phone;
        TimeText = string.IsNullOrWhiteSpace(detail.VisitCompletedAt) ? "—" : detail.VisitCompletedAt;
        HasTime = !string.IsNullOrWhiteSpace(detail.VisitCompletedAt);
        DoctorText = detail.VisitDoctorName;
        HasDoctor = detail.HasVisitContext && !string.IsNullOrWhiteSpace(detail.VisitDoctorName);
        InsuranceLabel = detail.InsuranceLabel;
        HasInsuranceLabel = detail.HasInsurance && !string.IsNullOrWhiteSpace(detail.InsuranceLabel);
        ItemCountText = SalePresentation.Fa(detail.Lines.Count);

        BaseText = SalePresentation.Money(detail.Base);
        DiscountText = "− " + SalePresentation.Money(detail.DiscountAmount);
        TaxText = "+ " + SalePresentation.Money(detail.Tax);
        InsuranceShareText = "− " + SalePresentation.Money(detail.InsuranceShare);
        TotalText = SalePresentation.Money(detail.Total);
        PaidText = SalePresentation.Money(detail.Paid);
        DueText = SalePresentation.Money(detail.Due);

        HasInsurance = detail.HasInsurance;
        HasDiscount = detail.HasDiscount;
        HasDue = detail.Due > 0;

        var editable = !detail.IsLocked;
        CanEdit = editable;
        ShowVoid = editable;
        ShowRefund = detail.State is InvoiceState.Partial or InvoiceState.Settled;
        CanPay = editable && detail.Lines.Count > 0 && detail.Paid < detail.Total;
        IsDeleted = detail.State == InvoiceState.Deleted;

        var canReturn = detail.State is InvoiceState.Partial or InvoiceState.Settled && detail.Paid > 0;
        ShowReturn = canReturn && detail.Lines.Count > 0;
        CanEditMeta = editable;
        ReceiptText = detail.ReceiptNo;
        HasReceipt = !string.IsNullOrEmpty(detail.ReceiptNo);

        Lines.ReplaceAll(detail.Lines.Select(line =>
            new InvoiceViewerLineViewModel(line, editable, canReturn, OnLineEdited, OnLineRemoved, OnLineReturned)));

        Payments.ReplaceAll(detail.Payments.Select(p => new InvoicePaymentViewModel(p)));
        HasPayments = Payments.Count > 0;
        PaymentsHeader = $"تاریخچهٔ پرداخت‌ها ({SalePresentation.Fa(Payments.Count)})";

        Audit.ReplaceAll(detail.Audit.Select(a => new InvoiceAuditRowViewModel(a)));
        HasAudit = Audit.Count > 0;
        AuditHeader = $"تاریخچه تغییرات ({SalePresentation.Fa(Audit.Count)})";

        _suppressInsuranceApply = true;
        DiscountKindIndex = detail.DiscountKind == DiscountKind.Amount ? 1 : 0;
        DiscountValueText = SalePresentation.Fa(detail.DiscountValue);
        SelectedInsurer = Insurers.FirstOrDefault(o => o.Id == detail.InsuranceOrgId) ?? Insurers.FirstOrDefault();
        _suppressInsuranceApply = false;
    }

    private void OnLineEdited(int index, int quantity, int discountPercent)
    {
        var title = LineTitle(index);
        var lineId = LineIdOf(index);

        // Ask before applying. If the user cancels, we snap the row's editable
        // fields back to the invoice's canonical values so the sheet never shows
        // an un-applied edit; the mutation only runs on confirm.
        Action revert = () =>
        {
            var line = _detail?.Lines.FirstOrDefault(l => l.Index == index);
            var row = Lines.FirstOrDefault(r => r.Index == index);
            if (line is not null && row is not null)
            {
                row.ResetEditFields(line.Quantity, line.DiscountPercent);
            }
        };

        _dialogs.ShowModal(new ConfirmDialogViewModel(
            _dialogs,
            "brand",
            "IconEdit",
            "ویرایش ردیف",
            "تعداد و تخفیف ",
            $"«{title}»",
            $" به {SalePresentation.Fa(quantity)} عدد و {SalePresentation.Fa(discountPercent)}٪ تخفیف تغییر کند؟",
            "مبلغ ردیف و جمع فاکتور بازمحاسبه و در تاریخچه تغییرات ثبت می‌شود.",
            "بله، اعمال کن",
            "انصراف",
            () => _ = MutateAsync(() => _service.EditLineAsync(_invoiceId, index, lineId, quantity, discountPercent))), 480);

        // Snap the editable fields back to canonical values immediately: if the
        // user cancels, the sheet shows no un-applied edit; if they confirm, the
        // mutation's Apply(...) re-applies the confirmed value.
        revert();
    }

    private void OnLineRemoved(int index)
    {
        var title = LineTitle(index);
        var lineId = LineIdOf(index);
        _dialogs.ShowModal(new ConfirmDialogViewModel(
            _dialogs,
            "danger",
            "IconTrash",
            "حذف ردیف",
            "ردیف ",
            $"«{title}»",
            " از فاکتور حذف شود؟",
            "این تغییر در تاریخچه تغییرات ثبت می‌شود.",
            "بله، حذف کن",
            "انصراف",
            () => _ = MutateAsync(() => _service.RemoveLineAsync(_invoiceId, index, lineId))), 480);
    }

    private void OnLineReturned(int index, int quantity)
    {
        var title = LineTitle(index);
        _dialogs.ShowModal(new ConfirmDialogViewModel(
            _dialogs,
            "warn",
            "IconArrowDownLeft",
            "برگشت کالا",
            "کالای ",
            $"«{title}»",
            $" ({SalePresentation.Fa(quantity)} عدد) برگشت داده شود؟",
            "در صورت پرداخت، مبلغ متناظر عودت و این تغییر در تاریخچه تغییرات ثبت می‌شود.",
            "بله، برگشت بزن",
            "انصراف",
            () => _ = ReturnLineAsync(index, quantity)), 480);
    }

    // The id of the line as it was rendered: the edit/remove is bound to this row, not to whatever
    // sits at the index by the time the user confirms (X27).
    private string LineIdOf(int index) =>
        _detail?.Lines.FirstOrDefault(l => l.Index == index)?.LineId ?? string.Empty;

    private string LineTitle(int index) =>
        _detail?.Lines.FirstOrDefault(l => l.Index == index)?.Title ?? "ردیف";

    private Task ReturnLineAsync(int index, int quantity) => GuardAsync("برگشت کالا ناموفق بود؛ دوباره تلاش کنید", async () =>
    {
        var result = await _service.ReturnLineAsync(_invoiceId, new LineReturnRequest(index, quantity));
        if (!result.Ok)
        {
            _toast.Show(Animora.Desktop.Localization.ErrorMessages.ForResult(result.Error, "خطا در برگشت کالا"), Tone.Danger);
            return;
        }

        Apply(result.Invoice);
        _toast.Show(result.Refunded > 0
            ? $"کالا برگشت خورد و {SalePresentation.Money(result.Refunded)} تومان عودت شد"
            : "کالا از فاکتور برگشت خورد", Tone.Ok);
        OnChanged();
    });

    private Task MutateAsync(Func<Task<InvoiceDetail?>> mutation) => GuardAsync("ویرایش فاکتور ناموفق بود؛ دوباره تلاش کنید", async () =>
    {
        var updated = await mutation();
        if (updated is not null)
        {
            Apply(updated);
        }

        _onChanged?.Invoke();
    });

    [RelayCommand]
    private void ApplyDiscount()
    {
        var kind = DiscountKindIndex == 1 ? DiscountKind.Amount : DiscountKind.Percent;
        var value = ParseIntStatic(DiscountValueText, 0);
        var valueLabel = kind == DiscountKind.Amount
            ? SalePresentation.Money(value) + " تومان"
            : SalePresentation.Fa(value) + "٪";

        _dialogs.ShowModal(new ConfirmDialogViewModel(
            _dialogs,
            "brand",
            "IconEdit",
            "اعمال تخفیف سراسری",
            "تخفیف سراسری ",
            $"«{valueLabel}»",
            " روی کل فاکتور اعمال شود؟",
            "مبلغ نهایی فاکتور بازمحاسبه و این تغییر در تاریخچه تغییرات ثبت می‌شود.",
            "بله، اعمال کن",
            "انصراف",
            () => _ = ApplyDiscountAsync(kind, value)), 480);
    }

    private Task ApplyDiscountAsync(DiscountKind kind, int value) => GuardAsync("اعمال تخفیف ناموفق بود؛ دوباره تلاش کنید", async () =>
    {
        var updated = await _service.SetDiscountAsync(_invoiceId, new DiscountChangeRequest(kind, value));
        if (updated is not null)
        {
            Apply(updated);
            _onChanged?.Invoke();
        }
    });

    private Task ApplyInsuranceAsync(string? insurerId) => GuardAsync("تغییر بیمه ناموفق بود؛ دوباره تلاش کنید", async () =>
    {
        var updated = await _service.SetInsuranceAsync(_invoiceId, insurerId);
        if (updated is not null)
        {
            Apply(updated);
            _onChanged?.Invoke();
        }
    });

    private static int ParseIntStatic(string? text, int fallback)
    {
        var ascii = PersianNumberFormatter.ToAsciiDigits(text).Trim();
        return int.TryParse(ascii, out var v) ? v : fallback;
    }

    // ── Footer actions ────────────────────────────────────────────────────

    [RelayCommand]
    private void TakePayment()
    {
        _dialogs.Close();
        _dialogs.ShowModal(new PaymentModalViewModel(_service, _toast, _dialogs, _print, _invoiceId, OnChanged), 520);
    }

    [RelayCommand]
    private void Refund()
    {
        _dialogs.Close();
        _dialogs.ShowModal(new RefundModalViewModel(_service, _toast, _dialogs, _invoiceId, _detail.Paid, OnChanged), 480);
    }

    [RelayCommand]
    private void VoidInvoice()
    {
        _dialogs.ShowModal(new ConfirmDialogViewModel(
            _dialogs,
            "danger",
            "IconX",
            "ابطال فاکتور",
            "آیا از ابطال فاکتور ",
            $"«{_detail.Id}»",
            " اطمینان دارید؟",
            "این عمل غیرقابل بازگشت است و در تاریخچه تغییرات ثبت می‌شود.",
            "بله، ابطال کن",
            "انصراف",
            () => _ = VoidConfirmedAsync()), 480);
    }

    /// <summary>
    /// Awaitable void handler (PERF §4 — no <c>async void</c>): a backend failure becomes a danger
    /// toast via <see cref="GuardAsync"/> instead of vanishing or crashing the process (X10).
    /// </summary>
    private Task VoidConfirmedAsync() => GuardAsync("ابطال فاکتور ناموفق بود؛ دوباره تلاش کنید", async () =>
    {
        var detail = await _service.VoidAsync(_invoiceId);
        if (detail is null)
        {
            _toast.Show("خطا در ابطال", Tone.Danger);
            return;
        }

        Apply(detail);
        _toast.Show($"فاکتور «{detail.Id}» ابطال شد", Tone.Ok);
        OnChanged();
    });

    [RelayCommand]
    private void DeleteInvoice()
    {
        _dialogs.ShowModal(new ConfirmDialogViewModel(
            _dialogs,
            "danger",
            "IconTrash",
            "حذف فاکتور",
            "آیا از حذف فاکتور ",
            $"«{_detail.Id}»",
            " اطمینان دارید؟",
            "فاکتور از لیست فعال حذف می‌شود، اما با تمام جزئیات در تاریخچه ثبت و قابل پیگیری می‌ماند.",
            "بله، حذف کن",
            "انصراف",
            () => _ = DeleteConfirmedAsync()), 480);
    }

    private Task DeleteConfirmedAsync() => GuardAsync("حذف فاکتور ناموفق بود؛ دوباره تلاش کنید", async () =>
    {
        var detail = await _service.DeleteInvoiceAsync(_invoiceId);
        if (detail is null)
        {
            _toast.Show("خطا در حذف فاکتور", Tone.Danger);
            return;
        }

        Apply(detail);
        _toast.Show($"فاکتور «{detail.Id}» حذف شد و در تاریخچه ثبت گردید", Tone.Ok);
        OnChanged();
    });

    /// <summary>
    /// Runs a confirmed viewer mutation: a typed backend failure is toasted as a danger message and the
    /// sheet re-reads the invoice so it never shows an un-applied edit (X09/X10).
    /// </summary>
    private async Task GuardAsync(string fallback, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (InvalidOperationException ex)
        {
            _toast.Show(
                Animora.Desktop.Pages.Patients.PatientsPresentation.CommandFailureMessage(ex, fallback),
                Tone.Danger);
            try
            {
                await ReloadAsync();
            }
            catch (InvalidOperationException)
            {
                // The failure toast was already shown; the next action re-syncs the sheet.
            }
        }
    }

    /// <summary>Opens the loaded invoice in the print preview (X13).</summary>
    [RelayCommand]
    private async Task Print()
    {
        if (_detail is null)
        {
            return;
        }

        await _print.OpenWithToastAsync(
            _toast, $"invoice-{_detail.Id}", SalesPrintDocuments.Invoice(_detail, _print.EmbeddedFontCss));
    }

    [RelayCommand]
    private void Close() => _dialogs.Close();

    private void OnChanged()
    {
        _onChanged?.Invoke();
        _ = ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        var detail = await _service.GetInvoiceAsync(_invoiceId);
        if (detail is not null)
        {
            Apply(detail);
        }
    }
}

/// <summary>
/// One line row of the invoice viewer: the kind tag, title/unit price, editable
/// quantity + discount boxes (writes back through the supplied callbacks) and the
/// derived subtotal.
/// </summary>
public sealed partial class InvoiceViewerLineViewModel : ObservableObject
{
    private readonly Action<int, int, int> _onEdited;
    private readonly Action<int> _onRemove;
    private readonly Action<int, int> _onReturn;
    private readonly int _quantity;
    private readonly int _discountPercent;

    public InvoiceViewerLineViewModel(
        InvoiceLine line, bool editable, bool canReturn,
        Action<int, int, int> onEdited, Action<int> onRemove, Action<int, int> onReturn)
    {
        _onEdited = onEdited;
        _onRemove = onRemove;
        _onReturn = onReturn;
        _quantity = line.Quantity;
        _discountPercent = line.DiscountPercent;
        Index = line.Index;
        Title = line.Title;
        IconKey = line.IconKey;
        KindLabel = line.KindLabel;
        UnitText = SalePresentation.Money(line.UnitPrice) + " تومان × " + SalePresentation.Fa(line.Quantity);
        IsEditable = editable;
        CanReturn = canReturn;
        _quantityText = SalePresentation.Fa(line.Quantity);
        _discountText = SalePresentation.Fa(line.DiscountPercent);
        (TagBackground, TagForeground) = SalePresentation.LineTagBrushes(line.Kind, line.IsCustom);
        SubtotalText = SalePresentation.Money(line.Subtotal);
    }

    public int Index { get; }

    public string Title { get; }

    public string IconKey { get; }

    public string KindLabel { get; }

    public string UnitText { get; }

    public bool IsEditable { get; }

    /// <summary>Whether the one-tap «برگشت» button is offered (the invoice is in a paid state).</summary>
    public bool CanReturn { get; }

    public IBrush TagBackground { get; }

    public IBrush TagForeground { get; }

    [ObservableProperty]
    private string _quantityText;

    [ObservableProperty]
    private string _discountText;

    [ObservableProperty]
    private string _subtotalText;

    public void SubmitEdit()
    {
        var quantity = ParseInt(QuantityText, 1);
        var discount = ParseInt(DiscountText, 0);

        // Only prompt/mutate when the value actually changed on blur — otherwise a
        // confirm would pop up every time focus leaves an untouched field.
        if (quantity == _quantity && discount == _discountPercent)
        {
            return;
        }

        _onEdited(Index, quantity, discount);
    }

    /// <summary>Snaps the editable quantity/discount fields back to canonical values (used when an edit confirm is cancelled).</summary>
    public void ResetEditFields(int quantity, int discountPercent)
    {
        QuantityText = SalePresentation.Fa(quantity);
        DiscountText = SalePresentation.Fa(discountPercent);
    }

    public void Remove() => _onRemove(Index);

    /// <summary>Returns the whole line quantity in one tap (mirrors «برگشت زدن فقط یک جنس»).</summary>
    public void Return() => _onReturn(Index, _quantity);

    private static int ParseInt(string? text, int fallback)
    {
        var ascii = PersianNumberFormatter.ToAsciiDigits(text).Trim();
        return int.TryParse(ascii, out var v) ? v : fallback;
    }
}

/// <summary>One audit-trail row of the invoice viewer («تاریخچه تغییرات»).</summary>
public sealed class InvoiceAuditRowViewModel
{
    public InvoiceAuditRowViewModel(InvoiceAuditEntry entry)
    {
        IconKey = InvoiceAuditPresentation.KindIcon(entry.Kind);
        BadgeBrush = SaleHistoryPresentation.Fill(InvoiceAuditPresentation.KindBg(entry.Kind));
        BadgeIconBrush = SaleHistoryPresentation.Fill(InvoiceAuditPresentation.KindColor(entry.Kind));
        Action = entry.Action;
        Detail = entry.Detail;
        HasDetail = !string.IsNullOrEmpty(entry.Detail);
        Time = SaleHistoryPresentation.FaText(entry.At);
    }

    public string IconKey { get; }

    public IBrush BadgeBrush { get; }

    public IBrush BadgeIconBrush { get; }

    public string Action { get; }

    public string Detail { get; }

    public bool HasDetail { get; }

    public string Time { get; }
}

/// <summary>The audit-kind → icon/tone registry for the viewer's change log.</summary>
public static class InvoiceAuditPresentation
{
    public static string KindIcon(InvoiceAuditKind kind) => kind switch
    {
        InvoiceAuditKind.Created => "IconReceipt",
        InvoiceAuditKind.ItemAdded => "IconPlus",
        InvoiceAuditKind.ItemEdited => "IconEdit",
        InvoiceAuditKind.ItemRemoved => "IconTrash",
        InvoiceAuditKind.DiscountChanged => "IconEdit",
        InvoiceAuditKind.InsuranceChanged => "IconShield",
        InvoiceAuditKind.Voided => "IconX",
        InvoiceAuditKind.Deleted => "IconTrash",
        InvoiceAuditKind.LineReturned => "IconArrowDownLeft",
        InvoiceAuditKind.PaymentTaken => "IconWallet",
        InvoiceAuditKind.Refunded => "IconArrowDownLeft",
        _ => "IconHistory",
    };

    public static string KindColor(InvoiceAuditKind kind) => kind switch
    {
        InvoiceAuditKind.Created => "#1570cd",
        InvoiceAuditKind.ItemAdded => "#027a48",
        InvoiceAuditKind.ItemEdited => "#6f57c9",
        InvoiceAuditKind.ItemRemoved => "#b54708",
        InvoiceAuditKind.DiscountChanged => "#6f57c9",
        InvoiceAuditKind.InsuranceChanged => "#0e7566",
        InvoiceAuditKind.Voided => "#d92d20",
        InvoiceAuditKind.Deleted => "#d92d20",
        InvoiceAuditKind.LineReturned => "#7b5cc4",
        InvoiceAuditKind.PaymentTaken => "#027a48",
        InvoiceAuditKind.Refunded => "#7b5cc4",
        _ => "#667085",
    };

    public static string KindBg(InvoiceAuditKind kind) => kind switch
    {
        InvoiceAuditKind.Created => "#eff8ff",
        InvoiceAuditKind.ItemAdded => "#ecfdf3",
        InvoiceAuditKind.ItemEdited => "#efeafb",
        InvoiceAuditKind.ItemRemoved => "#fffaeb",
        InvoiceAuditKind.DiscountChanged => "#efeafb",
        InvoiceAuditKind.InsuranceChanged => "#f0fdfa",
        InvoiceAuditKind.Voided => "#fef3f2",
        InvoiceAuditKind.Deleted => "#fef3f2",
        InvoiceAuditKind.LineReturned => "#f4f3ff",
        InvoiceAuditKind.PaymentTaken => "#ecfdf3",
        InvoiceAuditKind.Refunded => "#f4f3ff",
        _ => "#f1f5f6",
    };
}

/// <summary>One insurer option of the viewer's «پوشش بیمه» select (mirrors the Cashier's option VM).</summary>
public sealed class InsurerOptionViewModel
{
    public InsurerOptionViewModel(string id, string name)
    {
        Id = id;
        Label = string.IsNullOrEmpty(id) ? "بدون بیمه" : name;
    }

    public string Id { get; }

    public string Label { get; }
}
