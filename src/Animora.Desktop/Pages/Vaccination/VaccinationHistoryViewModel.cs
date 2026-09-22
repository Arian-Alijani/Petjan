using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Animora.Contracts.Vaccination;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Vaccination;

/// <summary>
/// The ViewModel for the «تاریخچه» section — the vaccination ledger
/// (administered / scheduled / SMS sent / rescheduled events). It mirrors the
/// Sales history ledger's paging model (period chips, kind chips, a count-only
/// summary bar and a virtualized feed) plus the toolbar capabilities the module
/// needs: a free date range, a page-size selector, a result sort and a
/// debounced search.
/// <para>
/// <b>Performance.</b> Every query is <b>server-side</b>: the filter + sort +
/// page window go to <see cref="IVaccinationService.GetHistoryAsync"/>, which
/// returns exactly one bounded page plus the total count computed over the whole
/// filtered set. The UI therefore only ever materialises one page of rows in a
/// virtualizing list (PERFORMANCE.md §6), never the whole ledger. Each reload
/// carries a generation token so a slow response can never overwrite a newer one
/// (stale-response guard), and the collections are rebuilt through a single
/// <c>ReplaceAll</c> notification (§5).
/// </para>
/// </summary>
public sealed partial class VaccinationHistoryViewModel : ObservableObject
{
    /// <summary>The page-size choices offered by the toolbar select.</summary>
    private static readonly int[] PageSizes = [25, 50, 100];

    private readonly IVaccinationService _service;
    private readonly Debouncer _searchDebounce = new(TimeSpan.FromMilliseconds(220));

    private readonly HashSet<VaccinationHistoryKind> _selectedKinds = [];

    private VaccinationHistorySort _sort = VaccinationHistorySort.Newest;
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

    public VaccinationHistoryViewModel(IVaccinationService service)
    {
        _service = service;
        BuildRangeChips();
        BuildKindChips();
    }

    public RangeObservableCollection<VaccinationHistoryRowViewModel> Rows { get; } = [];

    public ObservableCollection<VaccinationHistoryRangeViewModel> RangeChips { get; } = [];

    public ObservableCollection<VaccinationHistoryKindChipViewModel> KindChips { get; } = [];

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
    private string _pageText = "۰ / ۰";

    [ObservableProperty]
    private bool _hasPrev;

    [ObservableProperty]
    private bool _hasNext;

    public string SearchPlaceholder => "جستجو در بیمار، مالک یا واکسن…";

    /// <summary>Active sort select index (0..1), mirroring the toolbar's two options.</summary>
    public int SortIndex
    {
        get => (int)_sort;
        set
        {
            var clamped = Math.Clamp(value, 0, 1);
            if ((int)_sort != clamped)
            {
                _sort = (VaccinationHistorySort)clamped;
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
    /// Loads the ledger the first time the history section is shown. Kept lazy
    /// (not run from the constructor) so the page's eager load never pays for a
    /// section the user may not open (PERFORMANCE.md §12).
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

    /// <summary>Force a refresh (e.g. after a vaccination mutation changed the ledger).</summary>
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
        RangeChips.Add(new VaccinationHistoryRangeViewModel("today", "امروز", today, today));
        RangeChips.Add(new VaccinationHistoryRangeViewModel("7", "۷ روز اخیر", JalaliCalendar.AddDays(today, -6), today));
        RangeChips.Add(new VaccinationHistoryRangeViewModel("30", "۳۰ روز اخیر", JalaliCalendar.AddDays(today, -29), today));
        RangeChips.Add(new VaccinationHistoryRangeViewModel("all", "همه", string.Empty, string.Empty));
    }

    private void BuildKindChips()
    {
        KindChips.Clear();
        foreach (var kind in VaccinationHistoryPresentation.AllKinds)
        {
            KindChips.Add(new VaccinationHistoryKindChipViewModel(kind) { IsActive = _selectedKinds.Contains(kind) });
        }

        SyncAllKindsFlag();
    }

    /// <summary>Recomputes whether every kind is currently selected.</summary>
    private void SyncAllKindsFlag() =>
        AreAllKindsActive = KindChips.Count > 0 && _selectedKinds.Count == KindChips.Count;

    // ── Commands ─────────────────────────────────────────────────────────

    /// <summary>Applies a preset date range quick option.</summary>
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
    private void ToggleKind(VaccinationHistoryKind kind)
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
            var filter = new VaccinationHistoryFilter(
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

    private void ApplyPage(VaccinationHistoryPage page)
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

        Rows.ReplaceAll(page.Items.Select(entry => new VaccinationHistoryRowViewModel(entry)));

        HasRows = page.TotalCount > 0;
        IsEmpty = page.TotalCount == 0;

        var first = page.TotalCount == 0 ? 0 : ((_page - 1) * _pageSize) + 1;
        var last = (_page - 1) * _pageSize + page.Items.Count;

        SummaryCountText = page.TotalCount == 0
            ? "بدون نتیجه"
            : $"نمایش {VaccinationHistoryPresentation.Fa(first)}–{VaccinationHistoryPresentation.Fa(last)} از {VaccinationHistoryPresentation.Fa(page.TotalCount)} رکورد";

        PageText = $"{VaccinationHistoryPresentation.Fa(_page)} / {VaccinationHistoryPresentation.Fa(totalPages)}";
        HasPrev = _page > 1;
        HasNext = _page < totalPages;

        PrevPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
    }
}
