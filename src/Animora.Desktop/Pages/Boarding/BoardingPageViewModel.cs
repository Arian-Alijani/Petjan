using System.Collections.ObjectModel;
using Animora.Contracts.Boarding;
using Animora.Contracts.Diagnostics;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Services;
using Animora.Desktop.Shell;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Boarding;

/// <summary>
/// ViewModel for the Boarding screen (پانسیون). Mirrors the reference's page
/// controller (Animora-UI/assets/boarding.js): the page-chrome section switcher
/// (مدیریت اقامت / تاریخچه / تنظیمات), the status-tab filter with live counts
/// (<c>tabMatches</c>/<c>renderTabCounts</c>), the search + species filter, the
/// stay-cards grid (<c>renderCards</c>), the real History (<c>renderHistory</c>)
/// and Settings (<c>renderSettingsRooms</c>/<c>renderSettingsSvcs</c>) content,
/// and the detail / transfer / checkout / new-stay modal flows. All data flows
/// through <see cref="IBoardingQueryService"/> (the single backend swap point,
/// R-18) — no sample-data branching leaks into this public shape.
/// </summary>
public sealed partial class BoardingPageViewModel : PageViewModelBase
{
    private readonly IBoardingQueryService _service;
    private readonly IStoreReadinessGate _readiness;
    private readonly IToastService _toast;
    private readonly IDataChangeNotifier _changes;
    private readonly IDialogService _dialogs;
    private readonly IPrintService _print;
    private readonly INavigationService _navigation;

    private readonly List<BoardingStay> _stays = [];
    private readonly DateOnly _today = DateOnly.FromDateTime(DateTime.Today);

    // Reference page state: S.tab, S.query, S.species, S.section.
    private string _tab = "all";
    private int _staysVersion;
    private (int, string, BoardingSpecies?, string)? _renderedKey;
    private string _searchQuery = string.Empty;
    private BoardingSpecies? _species;

    public BoardingPageViewModel(IBoardingQueryService service, IStoreReadinessGate readiness, IToastService toast, IDialogService dialogs, INavigationService navigation,
        IDataChangeNotifier changes, IPrintService print)
    {
        _print = print;
        _service = service;
        _changes = changes;
        _readiness = readiness;
        _toast = toast;
        _dialogs = dialogs;
        _navigation = navigation;

        History = new BoardingHistoryViewModel(service);

        Title = "پانسیون";
        Eyebrow = "مدیریت کلینیک";
        Breadcrumbs.Add("مدیریت کلینیک");
        Breadcrumbs.Add("پانسیون");

        foreach (var (key, label, icon) in new[]
                 {
                     ("board", "مدیریت اقامت", "IconHome"),
                     ("history", "تاریخچه", "IconTimeline"),
                     ("settings", "تنظیمات", "IconTools"),
                 })
        {
            Sections.Add(new BoardingSectionViewModel(key, label, icon));
        }

        foreach (var (key, label) in new[]
                 {
                     ("all", "همه"),
                     ("current", "اقدام فعلی"),
                     ("overdue", "عقب‌افتاده"),
                     ("leaving", "تخلیه امروز"),
                     ("done", "تخلیه‌شده"),
                 })
        {
            Tabs.Add(new BoardingTabViewModel(key, label));
        }

        // Correct the «current» label to the reference's exact text.
        Tabs[1] = new BoardingTabViewModel("current", "اقامت فعلی");

        foreach (var (value, label) in new (BoardingSpecies?, string)[]
                 {
                     (null, "همه گونه‌ها"),
                     (BoardingSpecies.Dog, "سگ"),
                     (BoardingSpecies.Cat, "گربه"),
                     (BoardingSpecies.Bird, "پرنده"),
                     (BoardingSpecies.Rabbit, "خرگوش"),
                 })
        {
            var icon = value is { } species ? BoardingPresentation.SpeciesIconKey(species) : null;
            SpeciesOptions.Add(new BoardingSpeciesOption(value, label, icon));
        }

        SelectedSpeciesOption = SpeciesOptions[0];
        SelectedSpeciesOption.IsActive = true;

        UseLoadStatus(ReloadPageAsync);
        // Boarders are pets from the patient directory; checkout invoices are settled in the cashier.
        RefreshOnDataChange(changes, () => _ = ReloadPageAsync(), DataDomain.Patients, DataDomain.Cashier);
    }

    /// <summary>board | history | settings — the reference's <c>#bd-main-seg</c> keys.</summary>
    [ObservableProperty]
    private string _activeSection = "board";

    public bool IsBoardSection => ActiveSection == "board";
    public bool IsHistorySection => ActiveSection == "history";
    public bool IsSettingsSection => ActiveSection == "settings";

    /// <summary>Tier-2 board filters (status tabs + search + actions) are only on مدیریت اقامت.</summary>
    public bool ShowBoardTools => ActiveSection == "board";

    /// <summary>The «تاریخچه» ledger sub-VM (owned here, lazily loaded on section open).</summary>
    public BoardingHistoryViewModel History { get; }

    public ObservableCollection<BoardingSectionViewModel> Sections { get; } = [];
    public ObservableCollection<BoardingTabViewModel> Tabs { get; } = [];
    public ObservableCollection<BoardingSpeciesOption> SpeciesOptions { get; } = [];
    public RangeObservableCollection<BoardingStayCardViewModel> FilteredStays { get; } = [];

    /// <summary>
    /// <see cref="FilteredStays"/> chunked into rows of three cards each. The
    /// stay grid binds to this so a single-column virtualized ListBox still
    /// renders three cards per visible row (see <see cref="BoardingStayRowViewModel"/>).
    /// </summary>
    public RangeObservableCollection<BoardingStayRowViewModel> StayRows { get; } = [];

    [ObservableProperty]
    private BoardingSpeciesOption _selectedSpeciesOption;

    public string SpeciesLabelText => SelectedSpeciesOption.Label;

    [ObservableProperty]
    private bool _isSpeciesDropdownOpen;

    [ObservableProperty]
    private string _searchQueryText = string.Empty;

    [ObservableProperty]
    private bool _hasSearchValue;

    [ObservableProperty]
    private bool _hasStays;

    // ── Settings tab ────────────────────────────────────────────────────────
    public RangeObservableCollection<BoardingRoomTileViewModel> RoomTiles { get; } = [];
    public RangeObservableCollection<BoardingServiceRowViewModel> ServiceRows { get; } = [];

    partial void OnActiveSectionChanged(string value)
    {
        OnPropertyChanged(nameof(IsBoardSection));
        OnPropertyChanged(nameof(IsHistorySection));
        OnPropertyChanged(nameof(IsSettingsSection));
        OnPropertyChanged(nameof(ShowBoardTools));

        if (value == "history")
        {
            History.EnsureLoaded();
        }
    }

    partial void OnSelectedSpeciesOptionChanged(BoardingSpeciesOption value)
    {
        _species = value.Value;
        OnPropertyChanged(nameof(SpeciesLabelText));
        RefreshCards();
    }


    // Singleton page: reload on every visit (not only at construction) so data changed elsewhere is never stale.
    // Failures surface in the shell's load-state block, which also offers the manual refresh.
    public override void OnNavigatedTo()
    {
        if (!IsLoadInFlight) _ = ReloadPageAsync();
    }

    private Task ReloadPageAsync() => TrackLoadAsync(LoadAsync);

    private async Task LoadAsync()
    {
        await _readiness.WaitAsync();
        IsBusy = true;
        await ReloadStaysAsync();
        IsBusy = false;

        RefreshTabCounts();
        RefreshCards();
        BuildSettings();
    }

    private async Task ReloadStaysAsync()
    {
        var fresh = await _service.GetStaysAsync();
        _stays.Clear();
        _stays.AddRange(fresh);
        _staysVersion++;
    }

    private void RefreshTabCounts()
    {
        foreach (var tab in Tabs)
        {
            var count = _stays.Count(stay => TabMatches(stay, tab.Key));
            tab.CountText = PersianNumberFormatter.ToPersianDigits(count);
            tab.IsActive = tab.Key == _tab;
        }
    }

    private bool TabMatches(BoardingStay stay, string tab)
    {
        return BoardingStayClassification.Of(stay, _today).MatchesTab(tab);
    }

    private List<BoardingStay> Filtered(string query)
    {
        return _stays
            .Where(stay =>
            {
                if (!TabMatches(stay, _tab)) return false;
                if (_species is { } species && stay.Species != species) return false;
                if (query.Length > 0)
                {
                    var hay = PersianText.Normalize(
                        $"{stay.PetName} {stay.OwnerName} {stay.OwnerPhone} {stay.Breed} {stay.Room}");
                    if (!hay.Contains(query, StringComparison.Ordinal)) return false;
                }
                return true;
            })
            .OrderBy(stay => BoardingStayClassification.Of(stay, _today).IsOverdue ? 0 : 1)
            .ThenBy(stay => stay.Status == BoardingStatus.Done ? 1 : 0)
            .ThenBy(stay => stay.CheckOut)
            .ToList();
    }

    /// <summary>
    /// Rebuilds the card grid. Skips the filter+sort+chunk pass when neither the data
    /// (<see cref="_staysVersion"/>) nor the (tab, species, query) selection changed
    /// since the last render (PERF §13).
    /// </summary>
    private void RefreshCards()
    {
        var key = (_staysVersion, _tab, _species, PersianText.Normalize(_searchQuery));
        if (_renderedKey is { } last && last.Equals(key))
        {
            return;
        }
        _renderedKey = key;

        var rows = Filtered(key.Item4);
        var cards = rows.Select(stay => new BoardingStayCardViewModel(stay, _today)).ToList();
        FilteredStays.ReplaceAll(cards);

        var grouped = new List<BoardingStayRowViewModel>();
        for (var i = 0; i < cards.Count; i += 3)
        {
            grouped.Add(new BoardingStayRowViewModel(cards.GetRange(i, Math.Min(3, cards.Count - i))));
        }
        StayRows.ReplaceAll(grouped);

        HasStays = rows.Count > 0;
    }

    private void BuildSettings()
    {
        _ = RefreshRoomsAsync();
    }

    private async Task RefreshRoomsAsync()
    {
        RoomTiles.ReplaceAll(
            (await _service.GetRoomsAsync()).Select(room => new BoardingRoomTileViewModel(room)));

        ServiceRows.ReplaceAll(
            (await _service.GetServicesAsync()).Select(service => new BoardingServiceRowViewModel(service)));
    }

    /// <summary>Stay writes (incl. checkout charges) reach the cashier, the ledger and the dashboard; reload this page too.</summary>
    private void RefreshAfterWrite()
    {
        _changes.Notify(DataDomain.Boarding, DataDomain.Cashier, DataDomain.History, DataDomain.Dashboard);
        _ = RefreshAfterChangeAsync();
    }

    /// <summary>
    /// Consumed-item add/remove only changes the stay list (costs/badges): refresh that
    /// scope instead of stays + rooms + services + history (X37).
    /// </summary>
    private void RefreshAfterItemChange()
    {
        _changes.Notify(DataDomain.Boarding, DataDomain.Cashier, DataDomain.History, DataDomain.Dashboard);
        _ = RefreshStaysOnlyAsync();
    }

    private async Task RefreshStaysOnlyAsync()
    {
        await ReloadStaysAsync();
        RefreshTabCounts();
        RefreshCards();
    }

    private async Task RefreshAfterChangeAsync()
    {
        await ReloadStaysAsync();
        RefreshTabCounts();
        RefreshCards();
        History.Refresh();
        await RefreshRoomsAsync();
    }

    [RelayCommand]
    private void SetSection(string? key)
    {
        if (string.IsNullOrEmpty(key)) return;
        ActiveSection = key;
        IsSpeciesDropdownOpen = false;
    }

    [RelayCommand]
    private void SetTab(string? key)
    {
        if (string.IsNullOrEmpty(key) || key == _tab) return;
        _tab = key;
        RefreshTabCounts();
        RefreshCards();
    }

    [RelayCommand]
    private void ToggleSpecies() => IsSpeciesDropdownOpen = !IsSpeciesDropdownOpen;

    [RelayCommand]
    private void SetSpecies(BoardingSpeciesOption? option)
    {
        if (option is null) return;
        SelectedSpeciesOption = option;
        IsSpeciesDropdownOpen = false;
    }

    // Search debounce (~160ms), moved out of the code-behind into the VM.
    private readonly Debouncer _searchDebounce = new(System.TimeSpan.FromMilliseconds(160));

    partial void OnSearchQueryTextChanged(string value)
    {
        HasSearchValue = value.Trim().Length > 0;
        _searchDebounce.Debounce(() => ApplySearch(SearchQueryText));
    }

    public void ApplySearch(string? text)
    {
        _searchQuery = text?.Trim() ?? string.Empty;
        RefreshCards();
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchQueryText = string.Empty;
        HasSearchValue = false;
        ApplySearch(string.Empty);
    }

    // ── modal flows ─────────────────────────────────────────────────────────

    [RelayCommand]
    private void OpenStayDetail(BoardingStayCardViewModel? card)
    {
        if (card is null) return;
        ShowDetail(card.Stay.Id);
    }

    private void ShowDetail(string stayId) =>
        _dialogs.ShowModal(
            new StayDetailModalViewModel(
                _service, _toast, _dialogs, _print, stayId,
                onChanged: RefreshAfterItemChange,
                onTransfer: stay => ShowTransfer(stay),
                onCheckout: stay => ShowCheckout(stay),
                onGotoCashier: GotoCashier),
            maxWidth: 680);

    /// <summary>
    /// Reference (boarding.js <c>goto-cashier</c>): close the detail modal and
    /// navigate to the cashier/treasury screen. Phase 29 wires this to the real
    /// <see cref="Pages.Cashier.CashierPageViewModel"/> (the reference's target
    /// is <c>cashier.html</c>) instead of the old placeholder toast.
    /// </summary>
    private void GotoCashier()
    {
        _dialogs.Close();
        _navigation.NavigateTo<Animora.Desktop.Pages.Cashier.CashierPageViewModel>();
    }

    private void ShowTransfer(BoardingStay stay)
    {
        _ = ShowTransferAsync(stay);
    }

    private async Task ShowTransferAsync(BoardingStay stay)
    {
        var detail = await _service.GetDetailAsync(stay.Id);
        var items = detail?.Items ?? [];
        _dialogs.ShowModal(
            new TransferModalViewModel(_service, _toast, _dialogs, stay, items,
                onTransferred: RefreshAfterWrite),
            maxWidth: 580);
    }

    private void ShowCheckout(BoardingStay stay) =>
        _dialogs.ShowModal(
            new CheckoutModalViewModel(_service, _toast, _dialogs, stay,
                onCheckedOut: RefreshAfterWrite),
            maxWidth: 520);

    [RelayCommand]
    private void OpenNewStay() =>
        _dialogs.ShowModal(
            new NewStayModalViewModel(_service, _toast, _dialogs,
                onCreated: RefreshAfterWrite),
            maxWidth: 580);
}
