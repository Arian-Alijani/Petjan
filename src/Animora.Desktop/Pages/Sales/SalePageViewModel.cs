using System.Collections.ObjectModel;
using Animora.Contracts.Identity;
using Animora.Contracts.Sales;
using Animora.Domain.Identity;
using Animora.Contracts.Diagnostics;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Services;
using Animora.Desktop.Shell;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Sales;

/// <summary>
/// ViewModel for the Sale/POS invoice workspace. Mirrors the reference's page
/// controller (Animora-UI/assets/sale-page.js): the sidebar segmented switch
/// (<c>renderSeg</c>), the searchable ready-encounter / invoice lists
/// (<c>renderSidebar</c>, <c>matchQuery</c>/<c>matchEncQuery</c>), and the main
/// invoice work surface (<c>renderInvoiceContent</c>: header, line list via
/// <c>renderLine</c>, totals, terminal banner and payment history), plus the
/// inline mini-KPIs (<c>renderKPIs</c>). All data flows through
/// <see cref="ISalesQueryService"/> (the single backend swap point, R-18) — no
/// sample-data branching leaks into this public shape. The catalog
/// picker and payment modal are Phase 21; this phase shows the read-only invoice
/// display plus the empty work surface's funnel.
/// </summary>
public sealed partial class SalePageViewModel : PageViewModelBase
{
    private readonly ISalesQueryService _service;
    private readonly IChequePrompt _chequePrompt;
    private readonly IStoreReadinessGate _readiness;
    private readonly IToastService _toast;
    private readonly IDataChangeNotifier _changes;
    private readonly IDialogService _dialogs;
    private readonly IPrintService _print;
    private readonly ICapabilityGate _capabilities;

    /// <summary>
    /// D08: taking a payment / posting an invoice requires <c>sales.post</c>. Without it the CTA is not offered at
    /// all, instead of letting the operator click into a guaranteed <c>PERMISSION_DENIED</c> (the backend gate is
    /// unchanged and stays authoritative). Re-read per render, so a sign-in/sign-out is reflected.
    /// </summary>
    public bool CanPostPayments => _capabilities.Can(PermissionCodes.SalesPost);
    private readonly List<ReadyEncounterRef> _readyEncounters = [];
    private readonly List<InvoiceSummary> _invoices = [];

    private string _segment = "ready";

    /// <summary>The active workspace tab: <c>cashier</c> (the POS surface) or <c>history</c>.</summary>
    private string _workspace = "cashier";

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private SaleKpiSnapshot _kpi = new(0, 0, 0, 0);

    // Inline mini-KPIs (renderKPIs) — formatted for the header.
    [ObservableProperty]
    private string _settledTodayText = "۰";

    [ObservableProperty]
    private string _openDueText = "۰";

    [ObservableProperty]
    private string _openCountBadge = string.Empty;

    [ObservableProperty]
    private bool _hasOpenCountBadge;

    [ObservableProperty]
    private string _readyVisitText = "۰";

    [ObservableProperty]
    private string _readyVisitUnit = string.Empty;

    [ObservableProperty]
    private bool _hasReadyVisitBadge;

    public SalePageViewModel(ISalesQueryService service, IStoreReadinessGate readiness,
        IToastService toast, IDialogService dialogs, IDataChangeNotifier changes, IPrintService print,
        IHistoryRecordOpener historyOpener, IServiceRateService serviceRates, IChequePrompt chequePrompt,
        ICapabilityGate capabilities)
    {
        _chequePrompt = chequePrompt;
        _service = service;
        _print = print;
        _changes = changes;
        _readiness = readiness;
        _toast = toast;
        _dialogs = dialogs;
        _capabilities = capabilities;

        Title = "فروش و صندوق";
        Eyebrow = "مرکز فرمان";
        Breadcrumbs.Add("مرکز فرمان");
        Breadcrumbs.Add("فروش و صندوق");

        Segments.Add(new SaleSegmentViewModel("ready", "صف ویزیت"));
        Segments.Add(new SaleSegmentViewModel("invoices", "فاکتورها"));

        History = new SaleHistoryViewModel(service, historyOpener, NotifyInvoicesChanged, toast);
        Invoices = new SaleInvoiceLedgerViewModel(service, historyOpener, toast, dialogs, NotifyInvoicesChanged);
        Rates = new ServiceRatesViewModel(serviceRates, toast, dialogs, changes);

        UseLoadStatus(ReloadPageAsync);
        // The ready-visit queue is fed by completed encounters; the cashier settles the same invoices.
        RefreshOnDataChange(changes, () => _ = ReloadPageAsync(), DataDomain.Visits, DataDomain.Cashier);
        // The ledger tab lives inside this page: a sale/payment written elsewhere (cashier, visit sheet, owner
        // sheet) must show up without leaving and re-entering the tab. RefreshOnDataChange holds one subscription
        // per page, so the history tab keeps its own.
        // The page is a singleton for the app's lifetime, so the subscription is intentionally never released.
        _ = changes.Subscribe(domain =>
        {
            if (!IsCurrentPage || domain is not (DataDomain.History or DataDomain.Sales or DataDomain.Cashier))
            {
                return;
            }

            if (IsHistoryTab)
            {
                History.Refresh();
            }
            else if (IsInvoicesTab)
            {
                Invoices.Refresh();
            }
        });
    }

    /// <summary>
    /// The «تاریخچه صندوق» tab's ViewModel (accepted / paid / settled / cancelled
    /// / refunded ledger). Owned here so the workspace tab strip can swap the whole
    /// surface; its own data loads lazily on first activation.
    /// </summary>
    public SaleHistoryViewModel History { get; }

    /// <summary>
    /// The «فاکتور» tab's ViewModel — the invoice register (every invoice with its customer, source, date,
    /// money triplet and state, plus edit/delete for an open draft). Loads lazily on first activation.
    /// </summary>
    public SaleInvoiceLedgerViewModel Invoices { get; }

    /// <summary>The «نرخ خدمات» tab's ViewModel (clinic + boarding service rates); loads lazily on first activation.</summary>
    public ServiceRatesViewModel Rates { get; }

    public bool IsCashierTab => _workspace == "cashier";

    public bool IsInvoicesTab => _workspace == "invoices";

    public bool IsHistoryTab => _workspace == "history";

    public bool IsRatesTab => _workspace == "rates";

    /// <summary>True on the tabs whose toolbar sits flush under the tab row (invoices, history, rates).</summary>
    public bool IsFlushTab => IsInvoicesTab || IsHistoryTab || IsRatesTab;

    public ObservableCollection<SaleSegmentViewModel> Segments { get; } = [];

    public RangeObservableCollection<ReadyEncounterItemViewModel> ReadyList { get; } = [];

    /// <summary>Sidebar invoice rows — group headers interleaved with cards.</summary>
    public RangeObservableCollection<SaleSidebarRowViewModel> InvoiceRows { get; } = [];

    public RangeObservableCollection<InvoiceLineViewModel> Lines { get; } = [];

    public RangeObservableCollection<InvoicePaymentViewModel> Payments { get; } = [];

    public string SearchPlaceholder => "جستجو در ویزیت، فاکتور یا سرپرست…";

    public bool IsReadySegment => _segment == "ready";

    public bool IsInvoicesSegment => _segment == "invoices";

    public bool HasPayments => Payments.Count > 0;

    public bool IsReadyEmpty => IsReadySegment && ReadyList.Count == 0;

    public bool IsInvoicesEmpty => IsInvoicesSegment && InvoiceRows.Count == 0;

    public bool HasReadyEncounters => _readyEncounters.Count > 0;

    // ── Work surface state ──────────────────────────────────────────────

    [ObservableProperty]
    private SaleInvoiceViewModel? _selectedInvoice;

    [ObservableProperty]
    private bool _hasSelectedInvoice;

    [ObservableProperty]
    private bool _showWorkEmpty = true;

    /// <summary>The selected invoice's line count as Persian digits (= renderInvoiceContent's .ct).</summary>
    public string LineCountText => Lines.Count == 0 ? "۰" : SalePresentation.Fa(Lines.Count);

    /// <summary>The payment-history header («تاریخچهٔ پرداخت‌ها (N)»).</summary>
    public string HistoryHeaderText => $"تاریخچهٔ پرداخت‌ها ({SalePresentation.Fa(Payments.Count)})";

    // ── Invoice-level discount & insurance editors ─────────────────────

    /// <summary>The invoice-level discount kind toggle: <c>0</c> = «درصد», <c>1</c> = «مبلغ».</summary>
    [ObservableProperty]
    private int _discountKindIndex;

    /// <summary>The editable invoice-level discount value (Persian digits), seeded from the open invoice.</summary>
    [ObservableProperty]
    private string _discountValueText = "۰";

    /// <summary>The insurer options of the «پوشش بیمه» select («بدون بیمه» first).</summary>
    public RangeObservableCollection<InsurerOptionViewModel> Insurers { get; } = [];

    [ObservableProperty]
    private InsurerOptionViewModel? _selectedInsurer;

    private bool _suppressInsuranceApply;

    public bool IsDiscountPercent => DiscountKindIndex == 0;

    public bool IsDiscountAmount => DiscountKindIndex == 1;

    partial void OnDiscountKindIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsDiscountPercent));
        OnPropertyChanged(nameof(IsDiscountAmount));
    }

    partial void OnSelectedInsurerChanged(InsurerOptionViewModel? value)
    {
        if (_suppressInsuranceApply || SelectedInvoice is not { CanEdit: true } invoice)
        {
            return;
        }

        var chosenId = value?.Id ?? string.Empty;
        if (chosenId == invoice.Detail.InsuranceOrgId)
        {
            return;
        }

        _ = ApplyInsuranceAsync(invoice.Id, chosenId);
    }

    [RelayCommand]
    private void SetDiscountKind(string? index) => DiscountKindIndex = index == "1" ? 1 : 0;

    /// <summary>Applies the typed invoice-level discount (percent clamped to 0–100) and re-reads the invoice.</summary>
    [RelayCommand]
    private async Task ApplyDiscount()
    {
        if (SelectedInvoice is not { CanEdit: true } invoice)
        {
            return;
        }

        var kind = DiscountKindIndex == 1 ? DiscountKind.Amount : DiscountKind.Percent;
        var digits = PersianNumberFormatter.ToAsciiDigits(DiscountValueText).Replace(",", string.Empty).Replace("٬", string.Empty).Trim();
        if (!int.TryParse(digits, out var value) || value < 0)
        {
            _toast.Show("مقدار تخفیف معتبر نیست", Tone.Warn);
            return;
        }

        if (kind == DiscountKind.Percent && value > 100)
        {
            _toast.Show("تخفیف درصدی نمی‌تواند بیشتر از ۱۰۰٪ باشد", Tone.Warn);
            return;
        }

        try
        {
            await _service.SetDiscountAsync(invoice.Id, new DiscountChangeRequest(kind, value));
            _toast.Show("تخفیف فاکتور اعمال شد", Tone.Ok);
        }
        catch (InvalidOperationException ex)
        {
            ShowFailure(ex, "اعمال تخفیف ناموفق بود؛ دوباره تلاش کنید");
        }

        RefreshAfterMutation();
    }

    private async Task ApplyInsuranceAsync(string invoiceId, string insurerId)
    {
        try
        {
            await _service.SetInsuranceAsync(invoiceId, insurerId.Length == 0 ? null : insurerId);
            _toast.Show(insurerId.Length == 0 ? "پوشش بیمه برداشته شد" : "پوشش بیمه اعمال شد", Tone.Ok);
        }
        catch (InvalidOperationException ex)
        {
            ShowFailure(ex, "تغییر پوشش بیمه ناموفق بود؛ دوباره تلاش کنید");
        }

        RefreshAfterMutation();
    }

    /// <summary>Re-reads the insurer directory (so insurers added in «نرخ خدمات» appear) and re-selects the applied one.</summary>
    private async Task ReloadInsurersAsync()
    {
        var insurers = await _service.GetInsurersAsync();
        _suppressInsuranceApply = true;
        try
        {
            Insurers.ReplaceAll([new InsurerOptionViewModel(string.Empty, "بدون بیمه"),
                ..insurers.Select(i => new InsurerOptionViewModel(i.Id, i.Name))]);
            SyncAdjustmentEditors();
        }
        finally
        {
            _suppressInsuranceApply = false;
        }
    }

    /// <summary>Seeds the discount/insurance editors from the open invoice (never triggers a write).</summary>
    private void SyncAdjustmentEditors()
    {
        var wasSuppressed = _suppressInsuranceApply;
        _suppressInsuranceApply = true;
        try
        {
            var detail = SelectedInvoice?.Detail;
            DiscountKindIndex = detail?.DiscountKind == DiscountKind.Amount ? 1 : 0;
            DiscountValueText = SalePresentation.Fa(detail?.DiscountValue ?? 0);
            SelectedInsurer = Insurers.FirstOrDefault(o => o.Id == (detail?.InsuranceOrgId ?? string.Empty))
                ?? Insurers.FirstOrDefault();
        }
        finally
        {
            _suppressInsuranceApply = wasSuppressed;
        }
    }

    /// <summary>The tax summary label («مالیات (۹٪):»).</summary>
    public string TaxLabelText => SelectedInvoice is null
        ? "مالیات:"
        : $"مالیات ({SelectedInvoice.TaxPercentText}٪):";

    /// <summary>The smart pay-CTA label (renderPayCTA).</summary>
    public string PayCtaLabel => "پرداخت و تسویه کامل";

    private void RaiseDerived()
    {
        OnPropertyChanged(nameof(HasPayments));
        OnPropertyChanged(nameof(ShowWorkEmpty));
        OnPropertyChanged(nameof(IsReadyEmpty));
        OnPropertyChanged(nameof(IsInvoicesEmpty));
        OnPropertyChanged(nameof(HasReadyEncounters));
        OnPropertyChanged(nameof(LineCountText));
        OnPropertyChanged(nameof(HistoryHeaderText));
        OnPropertyChanged(nameof(TaxLabelText));
    }

    partial void OnHasSelectedInvoiceChanged(bool value)
    {
        ShowWorkEmpty = !value;
    }

    // ── Commands ────────────────────────────────────────────────────────

    /// <summary>
    /// Switches the top-level workspace tab — «صندوق» (the POS surface) or
    /// «تاریخچه» (the cashier ledger) or «نرخ خدمات» (service rates). The history
    /// and rates tabs load lazily on first activation (PERFORMANCE.md §12).
    /// </summary>
    [RelayCommand]
    private void SetWorkspace(string? key)
    {
        if (string.IsNullOrEmpty(key) || key == _workspace)
        {
            return;
        }

        _workspace = key;
        OnPropertyChanged(nameof(IsCashierTab));
        OnPropertyChanged(nameof(IsInvoicesTab));
        OnPropertyChanged(nameof(IsHistoryTab));
        OnPropertyChanged(nameof(IsRatesTab));
        OnPropertyChanged(nameof(IsFlushTab));

        if (IsInvoicesTab)
        {
            Invoices.EnsureLoaded();
        }
        else if (IsHistoryTab)
        {
            History.EnsureLoaded();
        }
        else if (IsRatesTab)
        {
            Rates.EnsureLoaded();
        }
    }

    [RelayCommand]
    private void SetSegment(string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        _segment = key;
        SearchText = string.Empty;
        OnPropertyChanged(nameof(IsReadySegment));
        OnPropertyChanged(nameof(IsInvoicesSegment));
        RenderSidebar();
    }

    [RelayCommand]
    private async Task SelectInvoice(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return;
        }

        InvoiceDetail? detail;
        try
        {
            detail = await _service.GetInvoiceAsync(id);
        }
        catch (InvalidOperationException ex)
        {
            ShowFailure(ex, "بازکردن فاکتور ناموفق بود؛ دوباره تلاش کنید");
            return;
        }

        if (detail is null)
        {
            return;
        }

        if (_segment != "invoices")
        {
            _segment = "invoices";
            OnPropertyChanged(nameof(IsReadySegment));
            OnPropertyChanged(nameof(IsInvoicesSegment));
        }

        RenderSidebar();
        ShowInvoice(detail);
    }

    [RelayCommand]
    private Task BuildFromEncounter(string? encounterId) => BuildFromEncounterAsync(encounterId);

    /// <summary>
    /// Issues the invoice for a completed encounter. Awaitable so the visits hand-off can await it
    /// through navigation; a backend failure (e.g. no hand-off owns the id) is surfaced as a danger
    /// toast instead of escaping onto the UI dispatcher as a crash (V05).
    /// </summary>
    public async Task BuildFromEncounterAsync(string? encounterId)
    {
        if (string.IsNullOrEmpty(encounterId))
        {
            return;
        }

        try
        {
            var detail = await _service.CreateFromEncounterAsync(encounterId);
            _segment = "invoices";
            OnPropertyChanged(nameof(IsReadySegment));
            OnPropertyChanged(nameof(IsInvoicesSegment));

            NotifyInvoicesChanged();
            await ReloadListsAsync();
            RenderSidebar();
            ShowInvoice(detail);
            await RefreshKpisAsync();
            _toast.Show($"فاکتور «{detail.Id}» از ویزیت صادر شد — آمادهٔ دریافت پرداخت", Tone.Ok);
        }
        catch (InvalidOperationException ex)
        {
            ShowFailure(ex, "صدور فاکتور از ویزیت ناموفق بود؛ از فهرست «آمادهٔ صدور» دوباره تلاش کنید");
        }
    }

    /// <summary>
    /// Opens the page on the «صف ویزیت» (ready-to-invoice) queue. Used by the visit / boarding
    /// hand-off: the doctor's account only marks the encounter ready, and the cashier issues the
    /// invoice from this list under the cashier's own <c>SalesDraft</c> permission. The page's own
    /// <see cref="OnNavigatedTo"/> reload then refreshes the queue contents.
    /// </summary>
    public void ShowReadyQueue()
    {
        _segment = "ready";
        SearchText = string.Empty;
        OnPropertyChanged(nameof(IsReadySegment));
        OnPropertyChanged(nameof(IsInvoicesSegment));
        if (_pageLoaded)
        {
            RenderSidebar();
        }
    }

    /// <summary>Surfaces a typed backend failure as a danger toast instead of letting it escape onto the UI dispatcher (X09).</summary>
    private void ShowFailure(InvalidOperationException ex, string fallback) =>
        _toast.Show(Animora.Desktop.Pages.Patients.PatientsPresentation.CommandFailureMessage(ex, fallback), Tone.Danger);

    [RelayCommand]
    private async Task NewBlankInvoice()
    {
        // Trace the submit so the log pairs this app-side action (sales.invoice.create for the guest customer)
        // with the server command the sync engine then pushes to /api/commands.
        Animora.Desktop.Diagnostics.AppTrace.Interaction("NewBlankInvoice", "click");

        InvoiceDetail detail;
        try
        {
            detail = await _service.CreateBlankAsync();
            NotifyInvoicesChanged();
            _segment = "invoices";
            OnPropertyChanged(nameof(IsReadySegment));
            OnPropertyChanged(nameof(IsInvoicesSegment));

            await ReloadListsAsync();
            RenderSidebar();
            ShowInvoice(detail);
            await RefreshKpisAsync();
        }
        catch (InvalidOperationException ex)
        {
            // The error code travels in the exception message (ENTITLEMENT_REQUIRED / SESSION_EXPIRED when the
            // server session was never established) — log it so the trace explains the failed "فاکتور جدید".
            Animora.Desktop.Diagnostics.AppTrace.Warn($"create invoice FAILED — {ex.Message}", "sales");
            ShowFailure(ex, "ایجاد فاکتور جدید ناموفق بود؛ دوباره تلاش کنید");
            return;
        }

        Animora.Desktop.Diagnostics.AppTrace.Info(
            $"create invoice OK — invoiceId={detail.Id}. Command stored locally; the sync engine will push it (watch for POST /api/commands).", "sales");

        // [Task 3] «فاکتور جدید» مستقیماً یک فاکتور گذریِ باز می‌سازد و نشان می‌دهد؛
        // دیگر مودالِ انتخاب مشتری باز نمی‌شود. تعویض مشتری از روی فاکتورِ باز
        // (ChangeCustomer) همچنان در دسترس است.
    }

    [RelayCommand]
    private void FunnelToVisits()
    {
        _segment = "ready";
        SearchText = string.Empty;
        OnPropertyChanged(nameof(IsReadySegment));
        OnPropertyChanged(nameof(IsInvoicesSegment));
        RenderSidebar();
        ShowEmptyWork();
    }

    [RelayCommand]
    private void AddItem()
    {
        if (SelectedInvoice is null)
        {
            return;
        }

        _dialogs.ShowModal(
            new CatalogModalViewModel(_service, _toast, _dialogs, SelectedInvoice.Id, RefreshAfterMutation),
            500);
    }

    [RelayCommand]
    private void AddCustomItem()
    {
        if (SelectedInvoice is null)
        {
            return;
        }

        _dialogs.ShowModal(
            new CustomItemModalViewModel(_service, _toast, _dialogs, SelectedInvoice.Id, false, RefreshAfterMutation),
            520);
    }

    [RelayCommand]
    private void TakePayment()
    {
        if (SelectedInvoice is null)
        {
            return;
        }

        _dialogs.ShowModal(
            new PaymentModalViewModel(_service, _toast, _dialogs, _print, _chequePrompt, SelectedInvoice.Id, RefreshAfterMutation, _capabilities),
            520);
    }

    /// <summary>Re-opens the owner/guest picker on the open invoice so a wrong customer can be swapped.</summary>
    [RelayCommand]
    private void ChangeCustomer()
    {
        if (SelectedInvoice is not { CanEdit: true } invoice)
        {
            return;
        }

        _dialogs.ShowModal(new OwnerPickerModalViewModel(
            _service, _toast, _dialogs, invoice.Id, RefreshAfterMutation), 560);
    }

    [RelayCommand]
    private void VoidInvoice()
    {
        if (SelectedInvoice is null)
        {
            return;
        }

        _dialogs.ShowModal(new Shared.ConfirmDialogViewModel(
            _dialogs,
            "danger",
            "IconTrash",
            "ابطال فاکتور",
            "آیا از ابطال فاکتور ",
            $"«{SelectedInvoice.Id}»",
            " اطمینان دارید؟",
            "این عمل غیرقابل بازگشت است و فاکتور از لیست فعال حذف می‌شود.",
            "بله، فاکتور را باطل کن",
            "انصراف",
            void () => _ = VoidAsync()), 480);
    }

    /// <summary>
    /// ✕ on an open (draft) invoice card: asks for confirmation, then deletes the draft entirely and
    /// clears the work surface if it was the one being edited.
    /// </summary>
    [RelayCommand]
    private void DeleteOpenInvoice(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        _dialogs.ShowModal(new Shared.ConfirmDialogViewModel(
            _dialogs,
            "danger",
            "IconTrash",
            "حذف فاکتور",
            "آیا از حذف فاکتور باز ",
            $"«{id}»",
            " اطمینان دارید؟",
            "فاکتور و اقلام آن به‌طور کامل حذف می‌شود و قابل بازگشت نیست.",
            "بله، فاکتور را حذف کن",
            "انصراف",
            void () => _ = DeleteOpenInvoiceAsync(id)), 480);
    }

    private async Task DeleteOpenInvoiceAsync(string id)
    {
        try
        {
            await _service.DeleteInvoiceAsync(id);
            _toast.Show($"فاکتور «{id}» حذف شد", Tone.Ok);
            NotifyInvoicesChanged();

            await ReloadListsAsync();
            RenderSidebar();
            if (SelectedInvoice?.Id == id)
            {
                ShowEmptyWork();
            }

            await RefreshKpisAsync();
        }
        catch (InvalidOperationException ex)
        {
            ShowFailure(ex, "حذف فاکتور ناموفق بود؛ فقط فاکتور پیش‌نویس قابل حذف است");
        }
    }

    private async Task VoidAsync()
    {
        if (SelectedInvoice is null)
        {
            return;
        }

        try
        {
            var detail = await _service.VoidAsync(SelectedInvoice.Id);
            if (detail is null)
            {
                _toast.Show("خطا در ابطال", Tone.Danger);
                return;
            }

            _toast.Show($"فاکتور «{detail.Id}» ابطال شد", Tone.Ok);
            NotifyInvoicesChanged();
            await RefreshAfterMutationAsync();
        }
        catch (InvalidOperationException ex)
        {
            ShowFailure(ex, "ابطال فاکتور ناموفق بود؛ دوباره تلاش کنید");
        }
    }

    [RelayCommand]
    private void Refund()
    {
        if (SelectedInvoice is null)
        {
            return;
        }

        _dialogs.ShowModal(new RefundModalViewModel(
            _service, _toast, _dialogs, SelectedInvoice.Id,
            SelectedInvoice.Detail.Paid, RefreshAfterMutation), 480);
    }

    /// <summary>Reloads the lists/KPIs re-renders sidebar and refreshes the open invoice.</summary>
    private void RefreshAfterMutation()
    {
        NotifyInvoicesChanged();
        _ = RefreshAfterMutationAsync();
    }

    /// <summary>A sale write also changes stock, the ledger, the cashier queue and the dashboard KPIs.</summary>
    private void NotifyInvoicesChanged() => _changes.Notify(
        DataDomain.Sales, DataDomain.Cashier, DataDomain.Inventory, DataDomain.Finance,
        DataDomain.History, DataDomain.Dashboard);

    private async Task RefreshAfterMutationAsync()
    {
        var currentId = SelectedInvoice?.Id;
        try
        {
            await ReloadListsAsync();
            RenderSidebar();
            await RefreshKpisAsync();

            // The register tab (if it was ever opened) shows the same invoices — keep it in step.
            Invoices.Refresh();

            if (currentId is not null)
            {
                var detail = await _service.GetInvoiceAsync(currentId);
                if (detail is not null)
                {
                    ShowInvoice(detail);
                }
            }
        }
        catch (InvalidOperationException ex)
        {
            // D16: the list reads now surface a store/session failure instead of quietly emptying the sidebar.
            // This refresh is fire-and-forget, so it must not let that escape: report it and keep the current view.
            Animora.Desktop.Diagnostics.AppTrace.Warn($"refresh after mutation failed — {ex.Message}", "sales");
            _toast.Show("به‌روزرسانی فهرست پس از ثبت انجام نشد؛ دوباره تلاش کنید", Tone.Warn);
        }
    }

    /// <summary>Opens the day report (every loaded invoice + the headline KPIs) in the print preview (X13).</summary>
    [RelayCommand]
    private async Task PrintDay()
    {
        var html = SalesPrintDocuments.DayReport(_invoices, Kpi, _print.EmbeddedFontCss);
        await _print.OpenWithToastAsync(_toast, "sales-day-report", html);
    }

    private readonly Debouncer _searchDebounce = new(System.TimeSpan.FromMilliseconds(160));

    partial void OnSearchTextChanged(string value) => _searchDebounce.Debounce(RenderSidebarIfQueryChanged);

    // The normalized query the sidebar was last rendered for; a keystroke that does not change it
    // (whitespace, an Arabic/Persian variant of the same letter) needs no rebuild (PERFORMANCE.md §13).
    private string _renderedQuery = string.Empty;

    private void RenderSidebarIfQueryChanged()
    {
        if (!string.Equals(PersianText.Normalize(SearchText), _renderedQuery, System.StringComparison.Ordinal))
        {
            RenderSidebar();
        }
    }

    // ── Loading ─────────────────────────────────────────────────────────

    // Singleton page: reload on every visit (not only at construction) so data changed elsewhere is never stale.
    // Failures surface in the shell's load-state block, which also offers the manual refresh.
    public override void OnNavigatedTo()
    {
        if (!IsLoadInFlight) _ = ReloadPageAsync();
        if (IsHistoryTab) History.Refresh();
        if (IsInvoicesTab) Invoices.Refresh();
    }

    private bool _pageLoaded;

    // First visit builds everything; later visits only re-read the data (keeps the open invoice).
    private Task ReloadPageAsync() => TrackLoadAsync(async () =>
    {
        if (_pageLoaded)
        {
            await RefreshAfterMutationAsync();
            return;
        }

        await LoadAsync();
        _pageLoaded = true;
    });

    private async Task LoadAsync()
    {
        await _readiness.WaitAsync();
        IsBusy = true;
        try
        {
            await ReloadListsAsync();
            RenderSidebar();
            await RefreshKpisAsync();
            // [Task 3] به‌جای صفحهٔ خالی، در بارِ اولِ ورود یک فاکتور گذریِ باز
            // به‌صورت پیش‌فرض نمایش داده می‌شود. ورودهای بعدی (صفحه singleton)
            // از مسیر RefreshAfterMutationAsync می‌روند و این متد دوباره صدا نمی‌خورد،
            // پس انباشتِ فاکتورهای خالی رخ نمی‌دهد.
            await OpenDefaultGuestInvoiceAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// [Task 3] پیش‌فرضِ ورود به صفحهٔ فروش و صندوق: یک فاکتور بازِ مشتری گذری.
    /// <see cref="ISalesQueryService.CreateBlankAsync"/> ذاتاً فاکتور را به‌عنوان
    /// مشتری گذری می‌سازد؛ پس نیازی به مودال انتخاب مشتری نیست. در صورت خطا به
    /// صفحهٔ خالی برمی‌گردیم تا کاربر دستی (فاکتور از ویزیت / فروش مستقیم) اقدام کند.
    /// </summary>
    private async Task OpenDefaultGuestInvoiceAsync()
    {
        try
        {
            var detail = await _service.CreateBlankAsync();
            NotifyInvoicesChanged();
            _segment = "invoices";
            OnPropertyChanged(nameof(IsReadySegment));
            OnPropertyChanged(nameof(IsInvoicesSegment));
            await ReloadListsAsync();
            RenderSidebar();
            ShowInvoice(detail);
        }
        catch (InvalidOperationException)
        {
            ShowEmptyWork();
        }
    }

    private async Task ReloadListsAsync()
    {
        _readyEncounters.Clear();
        _readyEncounters.AddRange(await _service.GetReadyEncountersAsync());

        _invoices.Clear();
        _invoices.AddRange(await _service.GetInvoicesAsync());

        await ReloadInsurersAsync();
    }

    private async Task RefreshKpisAsync()
    {
        Kpi = await _service.GetKpisAsync(_invoices, _readyEncounters.Count);
        SettledTodayText = SalePresentation.Money(Kpi.SettledTodayTotal);
        OpenDueText = SalePresentation.Money(Kpi.OpenDueTotal);

        HasOpenCountBadge = Kpi.OpenCount > 0;
        OpenCountBadge = SalePresentation.Fa(Kpi.OpenCount);

        ReadyVisitText = SalePresentation.Fa(Kpi.ReadyVisitCount);
        ReadyVisitUnit = Kpi.ReadyVisitCount > 0 ? "مورد" : string.Empty;
        HasReadyVisitBadge = Kpi.ReadyVisitCount > 0;
    }

    // ── Sidebar rendering ───────────────────────────────────────────────

    private void RenderSidebar()
    {
        // Normalize the query once per render, not once per row (PERFORMANCE.md §2).
        var query = PersianText.Normalize(SearchText);
        _renderedQuery = query;

        var readyCt = _readyEncounters.Count;
        var openCt = _invoices.Count(i => i.State is InvoiceState.Draft or InvoiceState.Partial);

        foreach (var seg in Segments)
        {
            seg.IsActive = seg.Key == _segment;
            seg.CountText = seg.Key == "ready"
                ? SalePresentation.Fa(readyCt)
                : SalePresentation.Fa(_invoices.Count);
            seg.IsPulsing = seg.Key == "ready" ? readyCt > 0 : openCt > 0;
        }

        if (_segment == "ready")
        {
            InvoiceRows.ReplaceAll([]);
            ReadyList.ReplaceAll(
                _readyEncounters.Where(enc => MatchesEncounter(enc, query)).Select(enc => new ReadyEncounterItemViewModel(enc)));
        }
        else
        {
            ReadyList.ReplaceAll([]);
            var filtered = _invoices.Where(inv => MatchesInvoice(inv, query)).ToList();
            var rows = new List<SaleSidebarRowViewModel>(filtered.Count + 3);
            AddGroup(rows, "باز و در انتظار", filtered.Where(i => i.State is InvoiceState.Draft or InvoiceState.Partial));
            AddGroup(rows, "تسویه‌شده", filtered.Where(i => i.State == InvoiceState.Settled));
            AddGroup(rows, "ابطال و عودت", filtered.Where(i => i.State is InvoiceState.Void or InvoiceState.Refunded));
            InvoiceRows.ReplaceAll(rows);
        }

        RaiseDerived();
    }

    private void AddGroup(List<SaleSidebarRowViewModel> target, string label, IEnumerable<InvoiceSummary> list)
    {
        var items = list.ToList();
        if (items.Count == 0)
        {
            return;
        }

        target.Add(SaleSidebarRowViewModel.Header(label, items.Count));
        foreach (var summary in items)
        {
            var row = SaleSidebarRowViewModel.Card(new InvoiceItemViewModel(summary));
            row.IsActive = SelectedInvoice is not null && row.InvoiceId == SelectedInvoice.Id;
            target.Add(row);
        }
    }

    private static bool MatchesInvoice(InvoiceSummary inv, string q)
    {
        return q.Length == 0 || PersianText.Contains(
            PersianText.Normalize($"{inv.Id} {inv.OwnerName} {inv.PetName} {inv.Phone}"), q);
    }

    private static bool MatchesEncounter(ReadyEncounterRef enc, string q)
    {
        return q.Length == 0 || PersianText.Contains(
            PersianText.Normalize($"{enc.Id} {enc.OwnerName} {enc.PetName} {enc.DoctorShort}"), q);
    }

    // ── Work surface ────────────────────────────────────────────────────

    private void ShowEmptyWork()
    {
        SelectedInvoice = null;
        Lines.ReplaceAll([]);
        Payments.ReplaceAll([]);
        HasSelectedInvoice = false;
        RaiseDerived();
    }

    /// <summary>Applies a new quantity to one line of the open invoice, then re-reads it (totals are recomputed by the service).</summary>
    private async Task EditLineAsync(string invoiceId, InvoiceLine line, int quantity)
    {
        if (quantity < 1)
        {
            return;
        }

        await MutateLineAsync(() => _service.EditLineAsync(invoiceId, line.Index, line.LineId, quantity, line.DiscountPercent),
            "ویرایش تعداد آیتم ناموفق بود؛ دوباره تلاش کنید");
    }

    private Task RemoveLineAsync(string invoiceId, InvoiceLine line) =>
        MutateLineAsync(() => _service.RemoveLineAsync(invoiceId, line.Index, line.LineId),
            "حذف آیتم ناموفق بود؛ دوباره تلاش کنید");

    // A stale index (the invoice changed underneath) and any other typed failure both end in a refresh, so the
    // user sees the invoice as it really is; the failure is also surfaced as a toast.
    private async Task MutateLineAsync(Func<Task<InvoiceDetail?>> mutate, string failureMessage)
    {
        try
        {
            await mutate();
        }
        catch (InvalidOperationException ex)
        {
            ShowFailure(ex, failureMessage);
        }

        RefreshAfterMutation();
    }

    public void ShowInvoice(InvoiceDetail detail)
    {
        SelectedInvoice = new SaleInvoiceViewModel(detail, CanPostPayments);
        HasSelectedInvoice = true;

        var canEdit = SelectedInvoice.CanEdit;
        Lines.ReplaceAll(detail.Lines.Select(line =>
            new InvoiceLineViewModel(line, canEdit, (l, qty) => EditLineAsync(detail.Id, l, qty),
                l => RemoveLineAsync(detail.Id, l))));
        Payments.ReplaceAll(detail.Payments.Select(payment => new InvoicePaymentViewModel(payment)));

        foreach (var row in InvoiceRows)
        {
            row.IsActive = !row.IsGroupHeader && row.InvoiceId == detail.Id;
        }

        SyncAdjustmentEditors();
        RaiseDerived();
    }
}
