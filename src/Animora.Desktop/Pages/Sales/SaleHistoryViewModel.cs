using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Animora.Contracts.Sales;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Services;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Sales;

/// <summary>
/// The ViewModel for the «تاریخچه صندوق» tab — the cashier-sales ledger
/// (accepted / paid / settled / cancelled / refunded events). It mirrors the
/// History screen's paging model (period chips, category chips, a summary bar and
/// a virtualized feed) and adds the toolbar capabilities the cashier needs: a
/// free date range («رجوع با تاریخ خاص»), a page-size selector, a result sort and
/// a free-text search.
/// <para>
/// <b>Performance.</b> Tens of thousands of ledger rows are expected over time,
/// so every query is <b>server-side</b>: the filter + sort + page window go to
/// <see cref="ISalesQueryService.GetHistoryAsync"/>, which returns exactly one
/// bounded page plus the totals. The UI therefore only ever materialises one page
/// of rows in a virtualizing list (PERFORMANCE.md §6), never the whole ledger.
/// Keystrokes are debounced (§13), each reload carries a generation token so a
/// slow response can never overwrite a newer one (stale-response guard), and the
/// collections are rebuilt through a single <c>ReplaceAll</c> notification (§5).
/// </para>
/// </summary>
public sealed partial class SaleHistoryViewModel : ObservableObject
{
    /// <summary>The page-size choices offered by the toolbar select.</summary>
    private static readonly int[] PageSizes = [25, 50, 100];

    private readonly ISalesQueryService _service;
    private readonly IToastService _toast;
    private readonly IDialogService _dialogs;
    private readonly Action? _onInvoiceChanged;
    private readonly Debouncer _searchDebounce = new(TimeSpan.FromMilliseconds(220));

    private readonly HashSet<CashierHistoryKind> _selectedKinds = [];

    private CashierHistorySort _sort = CashierHistorySort.Newest;
    private int _pageSize = PageSizes[0];
    private int _page = 1;
    private int _totalCount;
    private bool _loaded;

    // Set while dates are assigned programmatically (range chips / reset), so the
    // OnFromDateChanged/OnToDateChanged handlers do not fire a second reload.
    private bool _suppressDateReload;

    // Stale-response guard: bumped on every reload; a response whose token no
    // longer matches is discarded (a newer query has superseded it).
    private int _generation;

    public SaleHistoryViewModel(ISalesQueryService service, IToastService toast, IDialogService dialogs,
        Action? onInvoiceChanged = null)
    {
        _onInvoiceChanged = onInvoiceChanged;
        _service = service;
        _toast = toast;
        _dialogs = dialogs;
        BuildRangeChips();
        BuildKindChips();
    }

    /// <summary>
    /// Opens the center-screen invoice viewer for a ledger row's invoice, over
    /// the page. The viewer can edit/void/delete the invoice; each mutation is
    /// recorded in the audit trail, so refreshing the ledger afterwards shows
    /// the new «ویرایش»/«حذف» rows with their full detail.
    /// </summary>
    [RelayCommand]
    private void OpenInvoice(string? invoiceId)
    {
        if (string.IsNullOrEmpty(invoiceId))
        {
            return;
        }

        _dialogs.ShowSheet(
            new InvoiceViewerViewModel(_service, _toast, _dialogs, invoiceId, OnInvoiceChanged),
            1040,
            720);
    }

    public RangeObservableCollection<SaleHistoryRowViewModel> Rows { get; } = [];

    public ObservableCollection<SaleHistoryRangeViewModel> RangeChips { get; } = [];

    public ObservableCollection<SaleHistoryKindChipViewModel> KindChips { get; } = [];

    /// <summary>True when every kind filter is active — drives the «همه» chip's filled state.</summary>
    [ObservableProperty]
    private bool _areAllKindsActive;

    // ── Filter state ─────────────────────────────────────────────────────

    [ObservableProperty]
    private string _fromDate = string.Empty;

    [ObservableProperty]
    private string _toDate = string.Empty;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _hasSearchValue;

    [ObservableProperty]
    private bool _isLoading;

    // ── Summary / paging state ───────────────────────────────────────────

    [ObservableProperty]
    private bool _hasRows;

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private string _summaryCountText = string.Empty;

    [ObservableProperty]
    private string _summaryInflowText = string.Empty;

    [ObservableProperty]
    private string _summaryOutflowText = string.Empty;

    [ObservableProperty]
    private string _summaryNetText = string.Empty;

    [ObservableProperty]
    private IBrush _summaryNetBrush = SaleHistoryPresentation.Fill("#027a48");

    [ObservableProperty]
    private string _pageText = "۰ / ۰";

    [ObservableProperty]
    private bool _hasPrev;

    [ObservableProperty]
    private bool _hasNext;

    public string SearchPlaceholder => "جستجو در شمارهٔ فاکتور، مالک، بیمار یا توضیح…";

    /// <summary>Active sort select index (0..4), mirroring the toolbar's options.</summary>
    public int SortIndex
    {
        get => (int)_sort;
        set
        {
            var clamped = Math.Clamp(value, 0, 4);
            if ((int)_sort != clamped)
            {
                _sort = (CashierHistorySort)clamped;
                OnPropertyChanged();
                _ = ReloadAsync(resetPage: true);
            }
        }
    }

    /// <summary>Active page-size select index (0..2).</summary>
    public int PageSizeIndex
    {
        get => Array.IndexOf(PageSizes, _pageSize);
        set
        {
            var clamped = Math.Clamp(value, 0, PageSizes.Length - 1);
            var size = PageSizes[clamped];
            if (_pageSize != size)
            {
                _pageSize = size;
                OnPropertyChanged();
                _ = ReloadAsync(resetPage: true);
            }
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        HasSearchValue = !string.IsNullOrWhiteSpace(value);
        _searchDebounce.Debounce(() => _ = ReloadAsync(resetPage: true));
    }

    partial void OnFromDateChanged(string value) => OnDateRangeEdited();

    partial void OnToDateChanged(string value) => OnDateRangeEdited();

    private void OnDateRangeEdited()
    {
        // A programmatic date assignment (range chip / reset) already triggers its
        // own reload, so skip the handler's reload to avoid a duplicate query.
        if (_suppressDateReload)
        {
            return;
        }

        SyncRangeChips();
        _ = ReloadAsync(resetPage: true);
    }

    /// <summary>
    /// Loads the ledger the first time the history tab is shown. Kept lazy (not
    /// run from the constructor) so the page's eager load never pays for a tab
    /// the user may not open (PERFORMANCE.md §12).
    /// </summary>
    public void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        _ = ReloadAsync(resetPage: true);
    }

    // The viewer edited/voided/deleted an invoice: refresh the ledger and tell the other pages.
    private void OnInvoiceChanged()
    {
        Refresh();
        _onInvoiceChanged?.Invoke();
    }

    /// <summary>Force a refresh (e.g. after a cashier-tab mutation changed the ledger).</summary>
    public void Refresh()
    {
        if (!_loaded)
        {
            return;
        }

        _ = ReloadAsync(resetPage: false);
    }

    // ── Range / kind chip builders ───────────────────────────────────────

    private void BuildRangeChips()
    {
        var today = JalaliCalendar.Today;
        RangeChips.Clear();
        RangeChips.Add(new SaleHistoryRangeViewModel("today", "امروز", today, today));
        RangeChips.Add(new SaleHistoryRangeViewModel("7", "۷ روز اخیر", JalaliCalendar.AddDays(today, -6), today));
        RangeChips.Add(new SaleHistoryRangeViewModel("30", "۳۰ روز اخیر", JalaliCalendar.AddDays(today, -29), today));
        RangeChips.Add(new SaleHistoryRangeViewModel("all", "همه", string.Empty, string.Empty));
    }

    private void BuildKindChips()
    {
        KindChips.Clear();
        foreach (var kind in SaleHistoryPresentation.AllKinds)
        {
            KindChips.Add(new SaleHistoryKindChipViewModel(kind) { IsActive = _selectedKinds.Contains(kind) });
        }

        SyncAllKindsFlag();
    }

    /// <summary>Recomputes whether every kind is currently selected.</summary>
    private void SyncAllKindsFlag() =>
        AreAllKindsActive = KindChips.Count > 0 && _selectedKinds.Count == KindChips.Count;

    // ── Commands ─────────────────────────────────────────────────────────

    /// <summary>Applies a preset date range («رجوع با تاریخ خاص» quick option).</summary>
    [RelayCommand]
    private void SetRange(string? key)
    {
        var chip = RangeChips.FirstOrDefault(c => c.Key == key);
        if (chip is null)
        {
            return;
        }

        _suppressDateReload = true;
        FromDate = chip.From;
        ToDate = chip.To;
        _suppressDateReload = false;

        SyncRangeChips();
        _ = ReloadAsync(resetPage: true);
    }

    [RelayCommand]
    private void ClearRange()
    {
        _suppressDateReload = true;
        FromDate = string.Empty;
        ToDate = string.Empty;
        _suppressDateReload = false;

        SyncRangeChips();
        _ = ReloadAsync(resetPage: true);
    }

    [RelayCommand]
    private void ToggleKind(CashierHistoryKind kind)
    {
        if (!_selectedKinds.Add(kind))
        {
            _selectedKinds.Remove(kind);
        }

        foreach (var chip in KindChips)
        {
            chip.IsActive = _selectedKinds.Contains(chip.Kind);
        }

        SyncAllKindsFlag();
        _ = ReloadAsync(resetPage: true);
    }

    /// <summary>«همه» — activate every kind filter at once (or clear them when all are already on).</summary>
    [RelayCommand]
    private void SelectAllKinds()
    {
        var selectAll = !AreAllKindsActive;

        _selectedKinds.Clear();
        foreach (var chip in KindChips)
        {
            chip.IsActive = selectAll;
            if (selectAll)
            {
                _selectedKinds.Add(chip.Kind);
            }
        }

        SyncAllKindsFlag();
        _ = ReloadAsync(resetPage: true);
    }

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    [RelayCommand]
    private void ResetFilters()
    {
        _selectedKinds.Clear();
        _searchDebounce.Cancel();

        _suppressDateReload = true;
        SearchText = string.Empty;
        FromDate = string.Empty;
        ToDate = string.Empty;
        _suppressDateReload = false;

        HasSearchValue = false;
        foreach (var chip in KindChips)
        {
            chip.IsActive = false;
        }

        SyncAllKindsFlag();
        SyncRangeChips();
        _ = ReloadAsync(resetPage: true);
    }

    [RelayCommand]
    private void PrevPage()
    {
        if (_page <= 1)
        {
            return;
        }

        _page--;
        _ = ReloadAsync(resetPage: false);
    }

    [RelayCommand]
    private void NextPage()
    {
        if (!HasNext)
        {
            return;
        }

        _page++;
        _ = ReloadAsync(resetPage: false);
    }

    private void SyncRangeChips()
    {
        foreach (var chip in RangeChips)
        {
            chip.IsActive = chip.From == FromDate && chip.To == ToDate;
        }
    }

    // ── Loading ──────────────────────────────────────────────────────────

    private async Task ReloadAsync(bool resetPage)
    {
        if (!_loaded)
        {
            _loaded = true;
        }

        if (resetPage)
        {
            _page = 1;
        }

        var token = ++_generation;
        IsLoading = true;

        try
        {
            var filter = new CashierHistoryFilter(
                FromDate, ToDate, SearchText.Trim(), _selectedKinds.ToArray(), _sort, _page, _pageSize);

            var page = await _service.GetHistoryAsync(filter);

            // A newer reload started while this one was in flight — drop the
            // stale result rather than overwriting fresher state.
            if (token != _generation)
            {
                return;
            }

            ApplyPage(page);
        }
        finally
        {
            if (token == _generation)
            {
                IsLoading = false;
            }
        }
    }

    private void ApplyPage(CashierHistoryPage page)
    {
        _totalCount = page.TotalCount;

        var totalPages = Math.Max(1, (int)Math.Ceiling(page.TotalCount / (double)_pageSize));

        // If the set shrank below the current page (e.g. a filter narrowed while
        // paging), clamp to the last page and refetch once.
        if (_page > totalPages)
        {
            _page = totalPages;
            _ = ReloadAsync(resetPage: false);
            return;
        }

        Rows.ReplaceAll(page.Items.Select(entry => new SaleHistoryRowViewModel(entry)));

        HasRows = page.TotalCount > 0;
        IsEmpty = page.TotalCount == 0;

        var inflow = page.InflowTotal;
        var outflow = page.OutflowTotal;
        var net = inflow - outflow;

        var first = page.TotalCount == 0 ? 0 : ((_page - 1) * _pageSize) + 1;
        var last = (_page - 1) * _pageSize + page.Items.Count;

        SummaryCountText = page.TotalCount == 0
            ? "بدون نتیجه"
            : $"نمایش {SaleHistoryPresentation.Fa(first)}–{SaleHistoryPresentation.Fa(last)} از {SaleHistoryPresentation.Fa(page.TotalCount)} رکورد";
        SummaryInflowText = "+" + SaleHistoryPresentation.Money(inflow) + " ت";
        SummaryOutflowText = "−" + SaleHistoryPresentation.Money(outflow) + " ت";
        SummaryNetText = (net >= 0 ? "+" : "−") + SaleHistoryPresentation.Money(Math.Abs(net)) + " ت";
        SummaryNetBrush = SaleHistoryPresentation.Fill(net >= 0 ? "#027a48" : "#d92d20");

        PageText = $"{SaleHistoryPresentation.Fa(_page)} / {SaleHistoryPresentation.Fa(totalPages)}";
        HasPrev = _page > 1;
        HasNext = _page < totalPages;

        PrevPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
    }
}
