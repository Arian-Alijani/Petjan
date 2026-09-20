using System.Collections.ObjectModel;
using Animora.Contracts.Sales;
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
    private readonly IToastService _toast;
    private readonly IDialogService _dialogs;
    private readonly List<ReadyEncounterRef> _readyEncounters = [];
    private readonly List<InvoiceSummary> _invoices = [];

    private string _segment = "ready";

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

    public SalePageViewModel(ISalesQueryService service, IToastService toast, IDialogService dialogs)
    {
        _service = service;
        _toast = toast;
        _dialogs = dialogs;

        Title = "فروش و صندوق";
        Eyebrow = "مدیریت کلینیک";
        Breadcrumbs.Add("مدیریت کلینیک");
        Breadcrumbs.Add("فروش و صندوق");

        Segments.Add(new SaleSegmentViewModel("ready", "صف ویزیت"));
        Segments.Add(new SaleSegmentViewModel("invoices", "فاکتورها"));

        _ = LoadAsync();
    }

    public ObservableCollection<SaleSegmentViewModel> Segments { get; } = [];

    public ObservableCollection<ReadyEncounterItemViewModel> ReadyList { get; } = [];

    /// <summary>Sidebar invoice rows — group headers interleaved with cards.</summary>
    public ObservableCollection<SaleSidebarRowViewModel> InvoiceRows { get; } = [];

    public ObservableCollection<InvoiceLineViewModel> Lines { get; } = [];

    public ObservableCollection<InvoicePaymentViewModel> Payments { get; } = [];

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

        var detail = await _service.GetInvoiceAsync(id);
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
    private async Task BuildFromEncounter(string? encounterId)
    {
        if (string.IsNullOrEmpty(encounterId))
        {
            return;
        }

        var detail = await _service.CreateFromEncounterAsync(encounterId);
        _segment = "invoices";
        OnPropertyChanged(nameof(IsReadySegment));
        OnPropertyChanged(nameof(IsInvoicesSegment));

        await ReloadListsAsync();
        RenderSidebar();
        ShowInvoice(detail);
        await RefreshKpisAsync();
        _toast.Show($"فاکتور «{detail.Id}» از ویزیت صادر شد — آمادهٔ دریافت پرداخت", Tone.Ok);
    }

    [RelayCommand]
    private async Task NewBlankInvoice()
    {
        var detail = await _service.CreateBlankAsync(InvoiceKind.Sale);
        _segment = "invoices";
        OnPropertyChanged(nameof(IsReadySegment));
        OnPropertyChanged(nameof(IsInvoicesSegment));

        await ReloadListsAsync();
        RenderSidebar();
        ShowInvoice(detail);
        await RefreshKpisAsync();

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
            new PaymentModalViewModel(_service, _toast, _dialogs, SelectedInvoice.Id, RefreshAfterMutation),
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

        var detail = await _service.VoidAsync(SelectedInvoice.Id);
        if (detail is null)
        {
            _toast.Show("خطا در ابطال", Tone.Danger);
            return;
        }

        _toast.Show($"فاکتور «{detail.Id}» ابطال شد", Tone.Ok);
        await RefreshAfterMutationAsync();
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
    private void RefreshAfterMutation() => _ = RefreshAfterMutationAsync();

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

    [RelayCommand]
    private void PrintDay() => _toast.Show("گزارش روز به چاپگر ارسال شد (نمونه طراحی)", Tone.Ok);

    private readonly Debouncer _searchDebounce = new(System.TimeSpan.FromMilliseconds(160));

    partial void OnSearchTextChanged(string value) => _searchDebounce.Debounce(RenderSidebar);

    // ── Loading ─────────────────────────────────────────────────────────

    private async Task LoadAsync()
    {
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
        Kpi = await _service.GetKpisAsync();
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

        ReadyList.Clear();
        InvoiceRows.Clear();

        if (_segment == "ready")
        {
            foreach (var enc in _readyEncounters.Where(MatchesEncounter))
            {
                ReadyList.Add(new ReadyEncounterItemViewModel(enc));
            }
        }
        else
        {
            var filtered = _invoices.Where(MatchesInvoice).ToList();
            AddGroup("باز و در انتظار", filtered.Where(i => i.State is InvoiceState.Draft or InvoiceState.Partial));
            AddGroup("تسویه‌شده", filtered.Where(i => i.State == InvoiceState.Settled));
            AddGroup("ابطال و عودت", filtered.Where(i => i.State is InvoiceState.Void or InvoiceState.Refunded));
        }

        RaiseDerived();
    }

    private void AddGroup(string label, IEnumerable<InvoiceSummary> list)
    {
        var items = list.ToList();
        if (items.Count == 0)
        {
            return;
        }

        InvoiceRows.Add(SaleSidebarRowViewModel.Header(label, items.Count));
        foreach (var summary in items)
        {
            var row = SaleSidebarRowViewModel.Card(new InvoiceItemViewModel(summary));
            row.IsActive = SelectedInvoice is not null && row.InvoiceId == SelectedInvoice.Id;
            InvoiceRows.Add(row);
        }
    }

    private bool MatchesInvoice(InvoiceSummary inv)
    {
        var q = PersianText.Normalize(SearchText);
        return q.Length == 0 || PersianText.Contains(
            PersianText.Normalize($"{inv.Id} {inv.OwnerName} {inv.PetName} {inv.Phone}"), q);
    }

    private bool MatchesEncounter(ReadyEncounterRef enc)
    {
        var q = PersianText.Normalize(SearchText);
        return q.Length == 0 || PersianText.Contains(
            PersianText.Normalize($"{enc.Id} {enc.OwnerName} {enc.PetName} {enc.DoctorShort}"), q);
    }

    // ── Work surface ────────────────────────────────────────────────────

    private void ShowEmptyWork()
    {
        SelectedInvoice = null;
        Lines.Clear();
        Payments.Clear();
        HasSelectedInvoice = false;
        RaiseDerived();
    }

    public void ShowInvoice(InvoiceDetail detail)
    {
        SelectedInvoice = new SaleInvoiceViewModel(detail);
        HasSelectedInvoice = true;

        Lines.Clear();
        foreach (var line in detail.Lines)
        {
            Lines.Add(new InvoiceLineViewModel(line));
        }

        Payments.Clear();
        foreach (var payment in detail.Payments)
        {
            Payments.Add(new InvoicePaymentViewModel(payment));
        }

        foreach (var row in InvoiceRows)
        {
            row.IsActive = !row.IsGroupHeader && row.InvoiceId == detail.Id;
        }

        RaiseDerived();
    }
}
