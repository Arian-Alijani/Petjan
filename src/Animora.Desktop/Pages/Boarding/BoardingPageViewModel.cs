using System.Collections.ObjectModel;
using Animora.Contracts.Boarding;
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
/// (<c>tabMatches</c>/<c>renderTabCounts</c>), the search + species filter, and
/// the stay-cards grid (<c>renderCards</c>). All data flows through
/// <see cref="IBoardingQueryService"/> (the single backend swap point, R-18) —
/// no sample data or mock branching leaks into this public shape. The detail
/// and new-stay modals, and the real History/Settings content, are Phase 16's
/// material and show their placeholder for now.
/// </summary>
public sealed partial class BoardingPageViewModel : PageViewModelBase
{
    private readonly IBoardingQueryService _service;
    private readonly IToastService _toast;

    private readonly List<BoardingStay> _stays = [];
    private readonly DateOnly _today = DateOnly.FromDateTime(DateTime.Today);

    // Reference page state: S.tab, S.query, S.species, S.section.
    private string _tab = "all";
    private string _searchQuery = string.Empty;
    private BoardingSpecies? _species;

    public BoardingPageViewModel(IBoardingQueryService service, IToastService toast)
    {
        _service = service;
        _toast = toast;

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
                     ("current", "اقامت فعلی"),
                     ("overdue", "عقب‌افتاده"),
                     ("leaving", "تخلیه امروز"),
                     ("done", "تخلیه‌شده"),
                 })
        {
            Tabs.Add(new BoardingTabViewModel(key, label));
        }

        foreach (var (value, label) in new (BoardingSpecies?, string)[]
                 {
                     (null, "همه گونه‌ها"),
                     (BoardingSpecies.Dog, "سگ"),
                     (BoardingSpecies.Cat, "گربه"),
                     (BoardingSpecies.Bird, "پرنده"),
                     (BoardingSpecies.Rabbit, "خرگوش"),
                 })
        {
            var icon = value is { } species
                ? BoardingPresentation.SpeciesIconKey(species)
                : null;
            SpeciesOptions.Add(new BoardingSpeciesOption(value, label, icon));
        }

        SelectedSpeciesOption = SpeciesOptions[0];
        SelectedSpeciesOption.IsActive = true;

        _ = LoadAsync();
    }

    /// <summary>board | history | settings — the reference's <c>#bd-main-seg</c> keys.</summary>
    [ObservableProperty]
    private string _activeSection = "board";

    public bool IsBoardSection => ActiveSection == "board";
    public bool IsHistorySection => ActiveSection == "history";
    public bool IsSettingsSection => ActiveSection == "settings";

    /// <summary>Tier-2 board filters (status tabs + search + actions) are only on مدیریت اقامت.</summary>
    public bool ShowBoardTools => ActiveSection == "board";

    public ObservableCollection<BoardingSectionViewModel> Sections { get; } = [];
    public ObservableCollection<BoardingTabViewModel> Tabs { get; } = [];
    public ObservableCollection<BoardingSpeciesOption> SpeciesOptions { get; } = [];
    public ObservableCollection<BoardingStayCardViewModel> FilteredStays { get; } = [];

    [ObservableProperty]
    private BoardingSpeciesOption _selectedSpeciesOption;

    /// <summary>Label shown on the «فیلتر گونه» button (reference <c>#bd-species-label</c>).</summary>
    public string SpeciesLabelText => SelectedSpeciesOption.Label;

    /// <summary>Reference <c>#bd-species-dropdown</c> visibility (data-driven, toggled by the button).</summary>
    [ObservableProperty]
    private bool _isSpeciesDropdownOpen;

    [ObservableProperty]
    private string _searchQueryText = string.Empty;

    [ObservableProperty]
    private bool _hasSearchValue;

    [ObservableProperty]
    private bool _hasStays;

    partial void OnActiveSectionChanged(string value)
    {
        OnPropertyChanged(nameof(IsBoardSection));
        OnPropertyChanged(nameof(IsHistorySection));
        OnPropertyChanged(nameof(IsSettingsSection));
        OnPropertyChanged(nameof(ShowBoardTools));
    }

    partial void OnSelectedSpeciesOptionChanged(BoardingSpeciesOption value)
    {
        _species = value.Value;
        OnPropertyChanged(nameof(SpeciesLabelText));
        RefreshCards();
    }

    private async Task LoadAsync()
    {
        IsBusy = true;

        _stays.Clear();
        _stays.AddRange(await _service.GetStaysAsync());

        IsBusy = false;

        RefreshTabCounts();
        RefreshCards();
    }

    // Reference renderTabCounts: counts derive from ALL stays (not the filtered set).
    private void RefreshTabCounts()
    {
        foreach (var tab in Tabs)
        {
            var count = _stays.Count(stay => TabMatches(stay, tab.Key));
            tab.CountText = PersianNumberFormatter.ToPersianDigits(count);
            tab.IsActive = tab.Key == _tab;
        }
    }

    // Reference tabMatches(st).
    private bool TabMatches(BoardingStay stay, string tab)
    {
        var overdue = BoardingStayCardViewModel.IsStayOverdue(stay, _today);
        var leaving = BoardingStayCardViewModel.IsStayLeaving(stay, _today);

        return tab switch
        {
            "all" => true,
            "current" => stay.Status == BoardingStatus.Active && !leaving && !overdue,
            "overdue" => overdue,
            "leaving" => leaving && !overdue,
            "done" => stay.Status == BoardingStatus.Done,
            _ => true,
        };
    }

    // Reference filtered(): tab + species + query, then sort overdue-first,
    // active-before-done, then by check-out date.
    private List<BoardingStay> Filtered()
    {
        var query = PersianText.Normalize(_searchQuery);

        return _stays
            .Where(stay =>
            {
                if (!TabMatches(stay, _tab))
                {
                    return false;
                }

                if (_species is { } species && stay.Species != species)
                {
                    return false;
                }

                if (query.Length > 0)
                {
                    var hay = PersianText.Normalize(
                        $"{stay.PetName} {stay.OwnerName} {stay.OwnerPhone} {stay.Breed} {stay.Room}");
                    if (!hay.Contains(query, StringComparison.Ordinal))
                    {
                        return false;
                    }
                }

                return true;
            })
            .OrderBy(stay => BoardingStayCardViewModel.IsStayOverdue(stay, _today) ? 0 : 1)
            .ThenBy(stay => stay.Status == BoardingStatus.Done ? 1 : 0)
            .ThenBy(stay => stay.CheckOut)
            .ToList();
    }

    private void RefreshCards()
    {
        var rows = Filtered();

        FilteredStays.Clear();
        foreach (var stay in rows)
        {
            FilteredStays.Add(new BoardingStayCardViewModel(stay, _today));
        }

        HasStays = rows.Count > 0;
    }

    [RelayCommand]
    private void SetSection(string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        ActiveSection = key;
        IsSpeciesDropdownOpen = false;
    }

    [RelayCommand]
    private void SetTab(string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        _tab = key;
        RefreshTabCounts();
        RefreshCards();
    }

    /// <summary>Reference: the «فیلتر گونه» button toggles the species dropdown.</summary>
    [RelayCommand]
    private void ToggleSpecies() => IsSpeciesDropdownOpen = !IsSpeciesDropdownOpen;

    [RelayCommand]
    private void SetSpecies(BoardingSpeciesOption? option)
    {
        if (option is null)
        {
            return;
        }

        SelectedSpeciesOption = option;
        IsSpeciesDropdownOpen = false;
    }

    /// <summary>Debounced search is driven by the View; this just re-applies the filter (reference 160ms).</summary>
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

    // Reference openStayDetailModal → Phase 16 material; placeholder toast for now.
    [RelayCommand]
    private void OpenStayDetail(BoardingStayCardViewModel? card)
    {
        if (card is null)
        {
            return;
        }

        _toast.Show($"جزئیات اقامت «{card.Stay.PetName}» در فاز ۱۶ (Phase 16) ساخته می‌شود", Tone.Ok);
    }

    // Reference openNewStayModal → Phase 16 material; placeholder toast for now.
    [RelayCommand]
    private void OpenNewStay() =>
        _toast.Show("پذیرش اقامت جدید در فاز ۱۶ (Phase 16) ساخته می‌شود", Tone.Ok);
}
