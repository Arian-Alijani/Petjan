using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Animora.Contracts.Cashier;
using Animora.Contracts.Diagnostics;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Services;
using Animora.Desktop.Shell;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Cashier;

/// <summary>
/// ViewModel for the Cashier (invoice &amp; payment management) screen. Mirrors
/// the reference's page controller (Animora-UI/assets/cashier.js): the overview
/// stats (<c>renderStats</c>), the status-tab counters (<c>renderTabs</c>), the
/// searchable, tab-filtered, state-sorted invoice list (<c>renderList</c>), and
/// the invoice detail panel (<c>renderDetail</c>: lines via <c>renderLines</c>,
/// totals via <c>renderTotals</c>, the inline payment panel via
/// <c>renderPaymentPanel</c>, payment history via <c>renderPaymentHistory</c>
/// and the footer actions via <c>renderActions</c>). All data flows through
/// <see cref="ICashierService"/> (the single backend swap point, R-18) — no
/// sample-data branching leaks into this public shape.
/// </summary>
public sealed partial class CashierPageViewModel : PageViewModelBase
{
    private readonly ICashierService _service;
    private readonly IStoreReadinessGate _readiness;
    private readonly IToastService _toast;
    private readonly IDataChangeNotifier _changes;
    private readonly IPrintService _print;
    private readonly IDialogService _dialogs;

    private readonly List<CashierInvoice> _invoices = [];

    // Server-side paging (X29): _invoices holds only the pages loaded for the active tab + search.
    private const int ListPageSize = 50;
    private const int ListHardMax = 200;
    private int _listTotal;
    private int _listGeneration;
    private bool _suppressSelects;

    private CashierTab _tab = CashierTab.Open;

    public CashierPageViewModel(ICashierService service, IStoreReadinessGate readiness, IToastService toast,
        IDataChangeNotifier changes, IPrintService print, IDialogService dialogs)
    {
        _print = print;
        _dialogs = dialogs;
        _service = service;
        _changes = changes;
        _readiness = readiness;
        _toast = toast;

        Title = "صندوق و مالی";
        Eyebrow = "مدیریت کلینیک";
        Breadcrumbs.Add("مدیریت کلینیک");
        Breadcrumbs.Add("صندوق");

        UseLoadStatus(ReloadPageAsync);
        // Invoices are issued by the sales page; a completed encounter queues a new one.
        RefreshOnDataChange(changes, () => _ = ReloadPageAsync(), DataDomain.Sales, DataDomain.Visits);
    }

    public RangeObservableCollection<CashierStatViewModel> Stats { get; } = [];

    public RangeObservableCollection<CashierTabViewModel> Tabs { get; } = [];

    public RangeObservableCollection<CashierInvoiceRowViewModel> InvoiceRows { get; } = [];

    public RangeObservableCollection<CashierLineViewModel> Lines { get; } = [];

    public RangeObservableCollection<CashierPaymentViewModel> Payments { get; } = [];

    public RangeObservableCollection<CashierMethodViewModel> Methods { get; } = [];

    public RangeObservableCollection<CashierPresetViewModel> Presets { get; } = [];

    public RangeObservableCollection<CashierCatalogOptionViewModel> CatalogOptions { get; } = [];

    public RangeObservableCollection<CashierInsurerOptionViewModel> InsurerOptions { get; } = [];

    public RangeObservableCollection<CashierDiscountOptionViewModel> DiscountOptions { get; } = [];

    public string SearchPlaceholder => "جستجو در فاکتورها، مالکین یا حیوانات…";

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private CashierCatalogOptionViewModel? _selectedCatalogOption;

    [ObservableProperty]
    private CashierInsurerOptionViewModel? _selectedInsurerOption;

    [ObservableProperty]
    private CashierDiscountOptionViewModel? _selectedDiscountOption;

    [ObservableProperty]
    private string _discountValueText = "۰";

    // ── Detail state ────────────────────────────────────────────────────

    [ObservableProperty]
    private CashierDetailViewModel? _detail;

    [ObservableProperty]
    private bool _hasDetail;

    /// <summary>The default no-selection detail head text (= the reference's placeholder).</summary>
    public bool ShowDetailPlaceholder => !HasDetail;

    [ObservableProperty]
    private string _payAmountText = string.Empty;

    [ObservableProperty]
    private string _payReferenceText = string.Empty;

    [ObservableProperty]
    private string _payNoteText = string.Empty;

    [ObservableProperty]
    private string _refundAmountText = string.Empty;

    public bool IsEmpty => InvoiceRows.Count == 0;

    public bool ShowPaymentPanel => Detail is { IsEditable: true };

    public string PayAmountWatermark => Detail is null ? "۰" : CashierPresentation.Money(Detail.Detail.Due);

    partial void OnHasDetailChanged(bool value) => OnPropertyChanged(nameof(ShowDetailPlaceholder));

    partial void OnDetailChanged(CashierDetailViewModel? value)
    {
        OnPropertyChanged(nameof(ShowPaymentPanel));
        OnPropertyChanged(nameof(PayAmountWatermark));
    }

    private readonly Debouncer _searchDebounce = new(System.TimeSpan.FromMilliseconds(160));

    private string _appliedSearch = string.Empty;

    partial void OnSearchTextChanged(string value) => _searchDebounce.Debounce(ApplySearchIfChanged);

    // The search runs in the query, but only when the normalized text actually changed (PERFORMANCE.md §13).
    private void ApplySearchIfChanged()
    {
        var normalized = PersianText.Normalize(SearchText);
        if (string.Equals(normalized, _appliedSearch, System.StringComparison.Ordinal))
        {
            return;
        }

        _appliedSearch = normalized;
        _ = ReloadListSafeAsync(append: false);
    }

    partial void OnSelectedDiscountOptionChanged(CashierDiscountOptionViewModel? value)
    {
        if (!_suppressSelects && Detail is not null)
        {
            _ = SetDiscount();
        }
    }

    partial void OnSelectedInsurerOptionChanged(CashierInsurerOptionViewModel? value)
    {
        if (!_suppressSelects && Detail is not null)
        {
            _ = SetInsurance(value?.Id);
        }
    }

    private void RaiseDerived()
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CanLoadMore));
        OnPropertyChanged(nameof(ShowPaymentPanel));
        OnPropertyChanged(nameof(ShowDetailPlaceholder));
    }

    // ── Loading ─────────────────────────────────────────────────────────

    // Singleton page: reload on every visit (not only at construction) so data changed elsewhere is never stale.
    // Failures surface in the shell's load-state block, which also offers the manual refresh.
    public override void OnNavigatedTo()
    {
        if (!IsLoadInFlight) _ = ReloadPageAsync();
    }

    private bool _pageLoaded;

    // First visit builds everything; later visits only re-read the data (keeps the selected invoice and pay form).
    private Task ReloadPageAsync() => TrackLoadAsync(async () =>
    {
        if (_pageLoaded)
        {
            await ReloadAsync();
            return;
        }

        await LoadAsync();
        _pageLoaded = true;
    });

    private async Task LoadAsync()
    {
        IsBusy = true;
        await _readiness.WaitAsync();

        BuildMethods();
        BuildPresets();
        BuildDiscountOptions();

        CatalogOptions.ReplaceAll(
            (await _service.GetCatalogItemsAsync()).Select(item => new CashierCatalogOptionViewModel(item)));

        var insurers = new List<CashierInsurerOptionViewModel>
        {
            new(new CashierInsurer(string.Empty, string.Empty, 0), true),
        };
        insurers.AddRange(
            (await _service.GetInsurersAsync()).Select(insurer => new CashierInsurerOptionViewModel(insurer, false)));
        InsurerOptions.ReplaceAll(insurers);

        await ReloadAsync();
        IsBusy = false;
    }

    private Task ReloadAsync() => ReloadStatsAsync();

    private void BuildMethods()
    {
        Methods.ReplaceAll(
        [
            new CashierMethodViewModel(CashierPaymentMethod.Cash, "نقدی", "IconWallet"),
            new CashierMethodViewModel(CashierPaymentMethod.Card, "کارت", "IconCreditCard"),
            new CashierMethodViewModel(CashierPaymentMethod.Cheque, "چک", "IconCheque"),
            new CashierMethodViewModel(CashierPaymentMethod.Credit, "نسیه", "IconHourglass"),
            new CashierMethodViewModel(CashierPaymentMethod.Mixed, "ترکیبی", "IconLayers"),
        ]);
        SetMethod(CashierPaymentMethod.Cash);
    }

    private void BuildPresets()
    {
        Presets.ReplaceAll(new long[] { 50_000, 100_000, 200_000, 500_000, 1_000_000 }
            .Select(preset => new CashierPresetViewModel(CashierPresentation.Money(preset), preset, false)));
    }

    private void BuildDiscountOptions()
    {
        DiscountOptions.ReplaceAll(
        [
            new CashierDiscountOptionViewModel(CashierDiscountKind.Percent, "درصد (%)"),
            new CashierDiscountOptionViewModel(CashierDiscountKind.Amount, "مبلغ (ریال)"),
        ]);
    }

    // ── Tabs / list ─────────────────────────────────────────────────────

    [RelayCommand]
    private void SetTab(string? key)
    {
        if (string.IsNullOrEmpty(key) || !System.Enum.TryParse<CashierTab>(key, true, out var tab))
        {
            return;
        }

        _tab = tab;
        SearchText = string.Empty;
        _appliedSearch = string.Empty;
        _searchDebounce.Cancel();
        ShowEmptyDetail();
        foreach (var t in Tabs)
        {
            t.IsActive = t.Tab == _tab;
        }

        _ = ReloadListSafeAsync(append: false);
    }

    /// <summary>
    /// Fetches the active tab's rows from the query (X29): the first page (or as many rows as are already shown,
    /// so a post-mutation refresh keeps the user's place) or, with <paramref name="append"/>, the next page.
    /// Filtering, ordering and paging all happen in SQL; a generation token drops a slower stale response.
    /// </summary>
    private async Task ReloadListAsync(bool append)
    {
        var token = ++_listGeneration;
        var search = string.IsNullOrEmpty(_appliedSearch) ? null : _appliedSearch;

        CashierInvoicePage page;
        if (append)
        {
            var next = _invoices.Count / ListPageSize + 1;
            page = await _service.GetInvoicesAsync(_tab, search, next, ListPageSize);
        }
        else
        {
            var size = System.Math.Clamp(_invoices.Count, ListPageSize, ListHardMax);
            page = await _service.GetInvoicesAsync(_tab, search, 1, size);
        }

        if (token != _listGeneration)
        {
            return;
        }

        if (!append)
        {
            _invoices.Clear();
        }

        _invoices.AddRange(page.Items);
        _listTotal = page.TotalCount;
        RenderList();
    }

    private async Task ReloadListSafeAsync(bool append)
    {
        try
        {
            await ReloadListAsync(append);
        }
        catch (System.InvalidOperationException ex)
        {
            ShowFailure(ex, "بارگذاری فهرست فاکتورها ناموفق بود؛ دوباره تلاش کنید");
        }
    }

    /// <summary>Whether more rows exist beyond the pages already loaded for the active tab + search.</summary>
    public bool CanLoadMore => _invoices.Count < _listTotal;

    [RelayCommand]
    private Task LoadMore() => ReloadListSafeAsync(append: true);

    private void RenderList()
    {
        foreach (var tab in Tabs)
        {
            tab.IsActive = tab.Tab == _tab;
        }

        InvoiceRows.ReplaceAll(_invoices.Select(inv => new CashierInvoiceRowViewModel(inv)
        {
            IsActive = Detail is not null && Detail.Id == inv.Id,
        }));

        RaiseDerived();
    }

    // Selection only changes which row is highlighted — no rebuild of the page's rows (PERFORMANCE.md §13).
    private void MarkActiveRow()
    {
        foreach (var row in InvoiceRows)
        {
            row.IsActive = Detail is not null && Detail.Id == row.Id;
        }
    }

    // ── Selection ───────────────────────────────────────────────────────

    [RelayCommand]
    private async Task SelectInvoice(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return;
        }

        try
        {
            var detail = await _service.GetDetailAsync(id);
            if (detail is null)
            {
                return;
            }

            ResetPayment();
            ShowDetail(detail);
        }
        catch (System.InvalidOperationException ex)
        {
            ShowFailure(ex, "بازکردن فاکتور ناموفق بود؛ دوباره تلاش کنید");
        }
    }

    private void ResetPayment()
    {
        PayAmountText = string.Empty;
        PayReferenceText = string.Empty;
        PayNoteText = string.Empty;
        RefundAmountText = string.Empty;
        SetMethod(CashierPaymentMethod.Cash);
    }

    private void ShowDetail(CashierInvoiceDetail detail)
    {
        Detail = new CashierDetailViewModel(detail);
        HasDetail = true;

        DiscountValueText = CashierPresentation.Fa(detail.DiscountValue);
        _suppressSelects = true;
        SelectedDiscountOption = DiscountOptions.FirstOrDefault(o => o.Kind == detail.DiscountKind)
            ?? DiscountOptions.FirstOrDefault();
        SelectedInsurerOption = InsurerOptions.FirstOrDefault(o => !o.IsNone && o.Id == detail.InsuranceOrgId)
            ?? InsurerOptions.FirstOrDefault();
        _suppressSelects = false;

        BuildLines(detail);
        BuildPayments(detail);
        MarkActiveRow();
        RaiseDerived();
    }

    // The lines as last rendered; edit/remove callbacks resolve the LineId from this snapshot (X27).
    private System.Collections.Generic.IReadOnlyList<CashierInvoiceLine> _shownLines = [];

    private void ShowEmptyDetail()
    {
        _shownLines = [];
        Detail = null;
        HasDetail = false;
        Lines.Clear();
        Payments.Clear();
        RaiseDerived();
    }

    private void BuildLines(CashierInvoiceDetail detail)
    {
        _shownLines = detail.Lines;
        // Batched rebuild (PERFORMANCE.md §5): a single Reset, not one
        // notification per invoice line.
        Lines.ReplaceAll(detail.Lines.Select(line =>
            new CashierLineViewModel(line, detail.IsEditable, OnLineEdited, OnLineRemoved)));
    }

    private void BuildPayments(CashierInvoiceDetail detail)
    {
        // Batched rebuild (PERFORMANCE.md §5): a single Reset, not one
        // notification per payment.
        Payments.ReplaceAll(detail.Payments.Select(payment =>
            new CashierPaymentViewModel(payment)));
    }

    // Each edit is bound to the LineId of the row the cashier saw (X27), so a concurrent change surfaces as a
    // stale-line failure instead of editing a different line.
    private void OnLineEdited(int index, int quantity, int discountPercent)
    {
        var lineId = LineIdOf(index);
        _ = RunMutationAsync(() =>
            _service.UpdateLineAsync(Detail!.Id, index, lineId, quantity, discountPercent));
    }

    private void OnLineRemoved(int index)
    {
        var lineId = LineIdOf(index);
        _ = RunMutationAsync(() => _service.RemoveLineAsync(Detail!.Id, index, lineId));
    }

    private string LineIdOf(int index) =>
        _shownLines.FirstOrDefault(l => l.Index == index)?.LineId ?? string.Empty;

    // ── Payment panel ───────────────────────────────────────────────────

    [RelayCommand]
    private void PickMethod(CashierPaymentMethod method) => SetMethod(method);

    private void SetMethod(CashierPaymentMethod method)
    {
        foreach (var m in Methods)
        {
            m.IsOn = m.Method == method;
        }
    }

    private CashierPaymentMethod ActiveMethod =>
        Methods.FirstOrDefault(m => m.IsOn)?.Method ?? CashierPaymentMethod.Cash;

    [RelayCommand]
    private void UsePreset(long value)
    {
        PayAmountText = CashierPresentation.Fa(value);
    }

    [RelayCommand]
    private void UseFullAmount()
    {
        if (Detail is not null)
        {
            PayAmountText = CashierPresentation.Fa(Detail.Detail.Due);
        }
    }

    // ── Footer actions ──────────────────────────────────────────────────

    [RelayCommand]
    private async Task Pay()
    {
        if (Detail is null)
        {
            return;
        }

        var amount = ParseLong(PayAmountText);
        if (amount <= 0)
        {
            amount = Detail.Detail.Due;
        }

        var id = Detail.Id;
        var request = new CashierPayRequest(ActiveMethod, amount, PayReferenceText, PayNoteText);

        try
        {
            var result = await _service.PayAsync(id, request);

            if (!result.Ok)
            {
                _toast.Show(result.Error ?? "خطا در ثبت پرداخت", Tone.Danger);
                return;
            }

            var verb = result.Invoice?.State == CashierInvoiceState.Settled ? "تسویه شد" : "پرداخت ثبت شد";
            _toast.Show($"{verb} · {CashierPresentation.Money(result.Applied)}", Tone.Ok);

            ResetPayment();
            NotifyInvoicesChanged();
            await RefreshAsync(id);
        }
        catch (System.InvalidOperationException ex)
        {
            ShowFailure(ex, "ثبت پرداخت ناموفق بود؛ دوباره تلاش کنید");
        }
    }

    [RelayCommand]
    private async Task Void()
    {
        if (Detail is null)
        {
            return;
        }

        var id = Detail.Id;
        try
        {
            var result = await _service.VoidAsync(id);
            if (!result.Ok)
            {
                _toast.Show(result.Error ?? "خطا در ابطال", Tone.Danger);
                return;
            }

            _toast.Show("فاکتور ابطال شد", Tone.Ok);
            NotifyInvoicesChanged();
            await RefreshAsync(id);
        }
        catch (System.InvalidOperationException ex)
        {
            ShowFailure(ex, "ابطال فاکتور ناموفق بود؛ دوباره تلاش کنید");
        }
    }

    [RelayCommand]
    private async Task Refund()
    {
        if (Detail is null)
        {
            return;
        }

        var id = Detail.Id;
        var amount = ParseLong(RefundAmountText);
        if (amount <= 0)
        {
            amount = Detail.Detail.Paid;
        }

        try
        {
            var result = await _service.RefundAsync(id, new CashierRefundRequest(amount));
            if (!result.Ok)
            {
                _toast.Show(result.Error ?? "خطا در عودت", Tone.Danger);
                return;
            }

            _toast.Show($"عودت ثبت شد · {CashierPresentation.Money(result.Applied)}", Tone.Ok);
            RefundAmountText = string.Empty;
            NotifyInvoicesChanged();
            await RefreshAsync(id);
        }
        catch (System.InvalidOperationException ex)
        {
            ShowFailure(ex, "ثبت عودت ناموفق بود؛ دوباره تلاش کنید");
        }
    }

    [RelayCommand]
    private Task Print() => PrintSelectedInvoiceAsync();

    [RelayCommand]
    private Task PrintInvoice() => PrintSelectedInvoiceAsync();

    /// <summary>Opens the selected invoice in the print preview; with nothing selected it says so instead of faking success (X13).</summary>
    private async Task PrintSelectedInvoiceAsync()
    {
        if (Detail is null)
        {
            _toast.Show("ابتدا یک فاکتور را برای چاپ انتخاب کنید", Tone.Warn);
            return;
        }

        var html = CashierPrintDocuments.Invoice(Detail.Detail, _print.EmbeddedFontCss);
        await _print.OpenWithToastAsync(_toast, $"cashier-invoice-{Detail.Id}", html);
    }

    /// <summary>Saves the invoices currently listed (active tab + search) as a UTF-8 CSV that Excel opens directly (X13).</summary>
    [RelayCommand]
    private async Task Export()
    {
        if (_invoices.Count == 0)
        {
            _toast.Show("فاکتوری برای خروجی وجود ندارد", Tone.Warn);
            return;
        }

        var csv = CashierPrintDocuments.InvoicesCsv(_invoices);
        var name = "cashier-invoices-" + JalaliCalendar.Today.Replace('/', '-') + ".csv";
        var saved = await _dialogs.SaveTextFileAsync(name, csv, "csv");
        if (saved)
        {
            _toast.Show("خروجی فاکتورها ذخیره شد", Tone.Ok);
        }
    }

    // ── Inline add-line / discount / insurance ──────────────────────────

    [RelayCommand]
    private async Task AddLine()
    {
        if (Detail is null || SelectedCatalogOption is null)
        {
            return;
        }

        if (await RunMutationAsync(() => _service.AddLineAsync(Detail.Id, SelectedCatalogOption.Id)))
        {
            SelectedCatalogOption = null;
        }
    }

    [RelayCommand]
    private async Task SetDiscount()
    {
        if (Detail is null)
        {
            return;
        }

        var kind = SelectedDiscountOption?.Kind ?? CashierDiscountKind.Percent;
        var value = (int)ParseLong(DiscountValueText);
        await RunMutationAsync(() => _service.SetDiscountAsync(Detail.Id, kind, value));
    }

    [RelayCommand]
    private async Task SetInsurance(string? insurerId)
    {
        if (Detail is null)
        {
            return;
        }

        await RunMutationAsync(() => _service.SetInsuranceAsync(Detail.Id, insurerId));
    }

    /// <summary>
    /// Runs one inline invoice edit. A backend failure is surfaced as a danger toast (and the detail is
    /// re-read so the controls drop any rejected edit) instead of escaping onto the UI dispatcher (X09).
    /// </summary>
    private async Task<bool> RunMutationAsync(System.Func<Task<CashierInvoiceDetail>> mutation)
    {
        if (Detail is null)
        {
            return false;
        }

        var id = Detail.Id;
        try
        {
            var updated = await mutation();
            ShowDetail(updated);
            await ReloadStatsAsync();
            return true;
        }
        catch (System.InvalidOperationException ex)
        {
            ShowFailure(ex, "ویرایش فاکتور ناموفق بود؛ دوباره تلاش کنید");
            await TryRestoreDetailAsync(id);
            return false;
        }
    }

    private async Task TryRestoreDetailAsync(string id)
    {
        try
        {
            var detail = await _service.GetDetailAsync(id);
            if (detail is not null)
            {
                ShowDetail(detail);
            }
        }
        catch (System.InvalidOperationException)
        {
            // The failure toast was already shown; the next refresh re-syncs the panel.
        }
    }

    private void ShowFailure(System.InvalidOperationException ex, string fallback) =>
        _toast.Show(Animora.Desktop.Pages.Patients.PatientsPresentation.CommandFailureMessage(ex, fallback), Tone.Danger);

    /// <summary>A payment/void/refund moves stock-linked invoices, the ledger, the sales list and the dashboard KPIs.</summary>
    private void NotifyInvoicesChanged() => _changes.Notify(
        DataDomain.Cashier, DataDomain.Sales, DataDomain.Inventory, DataDomain.Finance,
        DataDomain.History, DataDomain.Dashboard);

    private async Task RefreshAsync(string currentId)
    {
        await ReloadStatsAsync();
        var detail = await _service.GetDetailAsync(currentId);
        if (detail is not null)
        {
            ShowDetail(detail);
        }
        else
        {
            ShowEmptyDetail();
        }
    }

    private async Task ReloadStatsAsync()
    {
        Stats.ReplaceAll(
            (await _service.GetStatsAsync()).Select(stat => new CashierStatViewModel(stat)));

        Tabs.ReplaceAll(
            (await _service.GetTabCountsAsync()).Select(count => new CashierTabViewModel(count)));

        // Always re-query and re-render the sidebar from the fresh data, independent of whether a detail is
        // still shown, so a void/refund moves the row to its new bucket without a second click (X20).
        await ReloadListAsync(append: false);
    }

    private static long ParseLong(string? text)
    {
        var ascii = PersianNumberFormatter.ToAsciiDigits(text).Replace("٬", string.Empty).Replace(",", string.Empty).Trim();
        return long.TryParse(ascii, out var v) ? v : 0;
    }
}
