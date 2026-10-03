using System.Collections.ObjectModel;
using Animora.Contracts.Sales;
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
/// sample data or mock branching leaks into this public shape. The catalog
/// picker and payment modal are Phase 21; this phase shows the read-only invoice
/// display plus the empty work surface's funnel.
/// </summary>
public sealed partial class SalePageViewModel : PageViewModelBase
{
    private readonly ISalesQueryService _service;
    private readonly IStoreReadinessGate _readiness;
    private readonly IToastService _toast;
    private readonly IDataChangeNotifier _changes;
    private readonly IDialogService _dialogs;
    private readonly IPrintService _print;
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
        IToastService toast, IDialogService dialogs, IDataChangeNotifier changes, IPrintService print)
    {
        _service = service;
        _print = print;
        _changes = changes;
        _readiness = readiness;
        _toast = toast;
        _dialogs = dialogs;

        Title = "فروش و صندوق";
        Eyebrow = "مدیریت کلینیک";
        Breadcrumbs.Add("مدیریت کلینیک");
        Breadcrumbs.Add("فروش و صندوق");

        Segments.Add(new SaleSegmentViewModel("ready", "صف ویزیت"));
        Segments.Add(new SaleSegmentViewModel("invoices", "فاکتورها"));

        History = new SaleHistoryViewModel(service, toast, dialogs, print, NotifyInvoicesChanged);

        UseLoadStatus(ReloadPageAsync);
        // The ready-visit queue is fed by completed encounters; the cashier settles the same invoices.
        RefreshOnDataChange(changes, () => _ = ReloadPageAsync(), DataDomain.Visits, DataDomain.Cashier);
    }

    /// <summary>
    /// The «تاریخچه صندوق» tab's ViewModel (accepted / paid / settled / cancelled
    /// / refunded ledger). Owned here so the workspace tab strip can swap the whole
    /// surface; its own data loads lazily on first activation.
    /// </summary>
    public SaleHistoryViewModel History { get; }

    public bool IsCashierTab => _workspace == "cashier";

    public bool IsHistoryTab => _workspace == "history";

    public ObservableCollection<SaleSegmentViewModel> Segments { get; } = [];

    public RangeObservableCollection<ReadyEncounterItemViewModel> ReadyList { get; } = [];

    /// <summary>Sidebar invoice rows — group headers interleaved with cards.</summary>
    public RangeObservableCollection<SaleSidebarRowViewModel> InvoiceRows { get; } = [];

    public RangeObservableCollection<InvoiceLineViewModel> Lines { get; } = [];

    public RangeObservableCollection<InvoicePaymentViewModel> Payments { get; } = [];

    public string SearchPlaceholder => "جستجو در ویزیت، فاکتور یا مالک…";

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

    /// <summary>The discount value in the adjustments row (= inv.discount.value).</summary>
    public string DiscountValueText => SelectedInvoice is null
        ? "۰"
        : SalePresentation.Fa(SelectedInvoice.Detail.DiscountValue);

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
        OnPropertyChanged(nameof(DiscountValueText));
        OnPropertyChanged(nameof(TaxLabelText));
    }

    partial void OnHasSelectedInvoiceChanged(bool value)
    {
        ShowWorkEmpty = !value;
    }

    // ── Commands ────────────────────────────────────────────────────────

    /// <summary>
    /// Switches the top-level workspace tab — «صندوق» (the POS surface) or
    /// «تاریخچه» (the cashier ledger). The history tab loads lazily on first
    /// activation (PERFORMANCE.md §12).
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
        OnPropertyChanged(nameof(IsHistoryTab));

        if (IsHistoryTab)
        {
            History.EnsureLoaded();
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

    /// <summary>Surfaces a typed backend failure as a danger toast instead of letting it escape onto the UI dispatcher (X09).</summary>
    private void ShowFailure(InvalidOperationException ex, string fallback) =>
        _toast.Show(Animora.Desktop.Pages.Patients.PatientsPresentation.CommandFailureMessage(ex, fallback), Tone.Danger);

    [RelayCommand]
    private async Task NewBlankInvoice()
    {
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
            ShowFailure(ex, "ایجاد فاکتور جدید ناموفق بود؛ دوباره تلاش کنید");
            return;
        }

        // A blank sale needs an owner/guest before items are added (mirrors the
        // reference's picker-first flow for walk-in sales).
        _dialogs.ShowModal(new OwnerPickerModalViewModel(
            _service, _toast, _dialogs, detail.Id, RefreshAfterMutation), 560);
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
            new PaymentModalViewModel(_service, _toast, _dialogs, _print, SelectedInvoice.Id, RefreshAfterMutation),
            520);
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
        await ReloadListsAsync();
        RenderSidebar();
        await RefreshKpisAsync();

        if (currentId is not null)
        {
            var detail = await _service.GetInvoiceAsync(currentId);
            if (detail is not null)
            {
                ShowInvoice(detail);
            }
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
            ShowEmptyWork();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ReloadListsAsync()
    {
        _readyEncounters.Clear();
        _readyEncounters.AddRange(await _service.GetReadyEncountersAsync());

        _invoices.Clear();
        _invoices.AddRange(await _service.GetInvoicesAsync());
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

    public void ShowInvoice(InvoiceDetail detail)
    {
        SelectedInvoice = new SaleInvoiceViewModel(detail);
        HasSelectedInvoice = true;

        Lines.ReplaceAll(detail.Lines.Select(line => new InvoiceLineViewModel(line)));
        Payments.ReplaceAll(detail.Payments.Select(payment => new InvoicePaymentViewModel(payment)));

        foreach (var row in InvoiceRows)
        {
            row.IsActive = !row.IsGroupHeader && row.InvoiceId == detail.Id;
        }

        RaiseDerived();
    }
}
