using System.Collections.ObjectModel;
using Animora.Contracts.Patients;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Services;
using Animora.Desktop.Shell;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Patients;

/// <summary>
/// ViewModel for the Patients &amp; Owners directory (Phase 08). Loads the
/// directory from <see cref="IPatientsQueryService"/> (the sole data seam) and
/// reproduces the reference's toolbar + list/grid behavior: a search box, a
/// derived species-chip filter rail, a pets/owners entity switch, a list/grid
/// layout toggle, a vaccination filter, a sort select, and the derived owner /
/// pet collections (mirroring <c>petMatches</c>/<c>ownerMatches</c>/<c>sortPets</c>).
/// </summary>
public sealed partial class PatientsPageViewModel : PageViewModelBase
{
    private readonly IPatientsQueryService _patientsQueryService;
    private readonly IRecordSheetPresenter _presenter;
    private readonly IDialogService _dialogs;
    private readonly IToastService _toast;
    private readonly IDataChangeNotifier _changes;
    private IReadOnlyList<PetSummary> _allPets = [];
    private IReadOnlyList<OwnerSummary> _allOwners = [];

    // O(1) lookup indexes (PERFORMANCE.md §2/§13): rebuilt once per data load in
    // RebuildIndexes(), never inside ApplyFilter. Mirrors the reference pattern in
    // AppointmentsCalendarViewModel.RebuildIndexes(). These turn the previous
    // O(owners × pets) filter into O(owners + pets).
    private readonly Dictionary<string, OwnerSummary> _ownersById = [];
    private readonly Dictionary<string, List<PetSummary>> _petsByOwnerId = [];

    // Pre-normalized per-item search haystacks: computed once at load time so the
    // per-keystroke ApplyFilter never rebuilds a StringBuilder/string per item.
    private readonly Dictionary<string, string> _petHaystack = [];
    private readonly Dictionary<string, string> _ownerHaystack = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private PatientViewMode _viewMode = PatientViewMode.Owners;

    [ObservableProperty]
    private PatientLayout _layout = PatientLayout.List;

    [ObservableProperty]
    private VaccineFilter _vaccineFilter = VaccineFilter.All;

    [ObservableProperty]
    private PatientSort _sort = PatientSort.Recent;

    [ObservableProperty]
    private PetSpecies? _selectedSpecies;

    // ── Pagination (PERFORMANCE.md §1/§13: cap realized rows for fast first
    // paint on large directories). The filter/sort pipeline runs over ALL
    // matches so counts stay accurate; only the current page is materialized
    // into the bound collections. "همه" (All) maps to AllPageSize.
    private const int AllPageSize = int.MaxValue;

    /// <summary>Selectable page sizes shown in the toolbar dropdown.</summary>
    public IReadOnlyList<PatientsPageSizeOption> PageSizeOptions { get; } =
    [
        new(25, "۲۵ پرونده"),
        new(50, "۵۰ پرونده"),
        new(100, "۱۰۰ پرونده"),
        new(AllPageSize, "همه"),
    ];

    // Default 50 (index 1) — the requested default visible count.
    [ObservableProperty]
    private PatientsPageSizeOption _selectedPageSize = new(50, "۵۰ پرونده");

    [ObservableProperty]
    private int _currentPage = 1;

    // Total matches for the active entity, before paging — drives the page
    // indicator, nav-button enable state, and the result counter.
    private int _totalMatchCount;

    public PatientsPageViewModel(
        IPatientsQueryService patientsQueryService,
        IRecordSheetPresenter presenter,
        IDialogService dialogs,
        IToastService toast,
        IDataChangeNotifier changes)
    {
        _patientsQueryService = patientsQueryService;
        _changes = changes;
        _presenter = presenter;
        _dialogs = dialogs;
        _toast = toast;
        Title = "بیماران و مالکین";
        Eyebrow = "مدیریت کلینیک";
        Breadcrumbs.Add("مدیریت کلینیک");
        Breadcrumbs.Add("بیماران");
        // Non-blocking init (PERFORMANCE.md §4): never .GetAwaiter().GetResult()
        // on the UI thread. The mock completes synchronously, so this still fills
        // the directory before the constructor returns, while a real async
        // backend keeps the shell responsive.
        UseLoadStatus(ReloadPageAsync);
        // Owner/pet sheets edit records from over this page; the page's own wizard/add-pet writes reload directly.
        RefreshOnDataChange(changes, () => _ = ReloadPageAsync(), DataDomain.Patients);
        _ = ReloadPageAsync();
    }

    public RangeObservableCollection<OwnerCardViewModel> FilteredOwners { get; } = [];
    public RangeObservableCollection<PetCardViewModel> FilteredPets { get; } = [];
    public RangeObservableCollection<OwnerCardRowViewModel> OwnerRows { get; } = [];
    public RangeObservableCollection<PetCardRowViewModel> PetRows { get; } = [];
    public RangeObservableCollection<PatientsSpeciesChipViewModel> SpeciesChips { get; } = [];

    public bool IsOwnersView => ViewMode == PatientViewMode.Owners;
    public bool IsPetsView => ViewMode == PatientViewMode.Pets;
    public bool IsGridView => Layout == PatientLayout.Grid;
    public bool IsListView => Layout == PatientLayout.List;
    public bool HasOwners => _totalOwnerMatches > 0;
    public bool HasPets => _totalPetMatches > 0;
    public bool HasSearchText => !string.IsNullOrWhiteSpace(SearchText);
    public string RecentSortLabel => IsOwnersView ? "جدیدترین ثبت" : "جدیدترین ویزیت";
    public string AgeSortLabel => IsOwnersView ? "قدیمی‌ترین ثبت" : "سن";

    // Content-region visibility: the four rendering combinations plus the
    // shared empty state (reference render() swaps host class + content).
    public bool ShowOwnersGrid => IsOwnersView && IsGridView && HasOwners;
    public bool ShowOwnersTable => IsOwnersView && IsListView && HasOwners;
    public bool ShowPetsGrid => IsPetsView && IsGridView && HasPets;
    public bool ShowPetsTable => IsPetsView && IsListView && HasPets;
    public bool ShowEmptyState => (IsOwnersView && !HasOwners) || (IsPetsView && !HasPets);
    public string EmptyTitle => IsOwnersView ? "مالکی پیدا نشد" : "بیماری پیدا نشد";

    // ── Pagination-derived state ──────────────────────────────────────────
    private int PageSize => SelectedPageSize?.Value ?? 50;
    private bool ShowAll => PageSize >= AllPageSize;

    /// <summary>Total number of pages for the active entity (>= 1).</summary>
    public int PageCount => ShowAll || _totalMatchCount == 0
        ? 1
        : (int)Math.Ceiling(_totalMatchCount / (double)PageSize);

    /// <summary>Only paginate when there is more than one page of matches.</summary>
    public bool ShowPagination => !ShowEmptyState && PageCount > 1;

    public bool CanGoPrevious => CurrentPage > 1;
    public bool CanGoNext => CurrentPage < PageCount;

    /// <summary>Page indicator text, e.g. «صفحه ۲ از ۵».</summary>
    public string PageIndicatorText =>
        $"صفحه {PersianNumberFormatter.ToPersianDigits(CurrentPage)} از "
        + $"{PersianNumberFormatter.ToPersianDigits(PageCount)}";

    /// <summary>Range caption, e.g. «۵۱ تا ۱۰۰ از ۲۴۰».</summary>
    public string PageRangeText
    {
        get
        {
            if (_totalMatchCount == 0)
            {
                return string.Empty;
            }

            var from = ShowAll ? 1 : ((CurrentPage - 1) * PageSize) + 1;
            var to = ShowAll ? _totalMatchCount : Math.Min(CurrentPage * PageSize, _totalMatchCount);
            return $"{PersianNumberFormatter.ToPersianDigits(from)}–{PersianNumberFormatter.ToPersianDigits(to)}"
                 + $" از {PersianNumberFormatter.ToPersianDigits(_totalMatchCount)}";
        }
    }

    // Full match totals (before paging) per entity — kept so the counter and
    // page math reflect every match, not just the realized page slice.
    private int _totalOwnerMatches;
    private int _totalPetMatches;

    /// <summary>Result counter text (reference toolbar .pb-count).</summary>
    public string ResultCountText => ViewMode == PatientViewMode.Owners
        ? $"{PersianNumberFormatter.ToPersianDigits(_totalOwnerMatches)} مالک · "
          + $"{PersianNumberFormatter.ToPersianDigits(_totalPetMatches)} بیمار"
        : $"{PersianNumberFormatter.ToPersianDigits(_totalPetMatches)} بیمار";

    /// <summary>Active state of the vaccination filter select (index 0..2).</summary>
    public int VaccineFilterIndex
    {
        get => (int)VaccineFilter;
        set => VaccineFilter = (VaccineFilter)Math.Clamp(value, 0, 2);
    }

    /// <summary>Active state of the sort select (index 0..2).</summary>
    public int SortIndex
    {
        get => (int)Sort;
        set => Sort = (PatientSort)Math.Clamp(value, 0, 2);
    }

    private readonly Debouncer _searchDebounce = new(System.TimeSpan.FromMilliseconds(160));

    partial void OnSearchTextChanged(string value) => _searchDebounce.Debounce(ApplyFilter);

    partial void OnViewModeChanged(PatientViewMode value) => ApplyFilter();

    partial void OnLayoutChanged(PatientLayout value) => NotifyDerived();

    partial void OnVaccineFilterChanged(VaccineFilter value)
    {
        OnPropertyChanged(nameof(VaccineFilterIndex));
        ApplyFilter();
    }

    partial void OnSortChanged(PatientSort value)
    {
        OnPropertyChanged(nameof(SortIndex));
        ApplyFilter();
    }

    // Changing page size (or any filter/sort/view) resets to page 1 and re-slices.
    partial void OnSelectedPageSizeChanged(PatientsPageSizeOption value)
    {
        CurrentPage = 1;
        ApplyFilter();
    }

    partial void OnCurrentPageChanged(int value) => NotifyPaginationDerived();

    /// <summary>Go to the next page of results (toolbar/footer nav).</summary>
    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void NextPage()
    {
        if (CanGoNext)
        {
            CurrentPage++;
            RematerializePage();
        }
    }

    /// <summary>Go to the previous page of results.</summary>
    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private void PreviousPage()
    {
        if (CanGoPrevious)
        {
            CurrentPage--;
            RematerializePage();
        }
    }

    [RelayCommand]
    private void ToggleViewMode() =>
        ViewMode = ViewMode == PatientViewMode.Owners ? PatientViewMode.Pets : PatientViewMode.Owners;

    /// <summary>Raised by the toolbar's owners/pets segmented switch.</summary>
    [RelayCommand]
    private void SetViewMode(string? mode) =>
        ViewMode = mode == "pets" ? PatientViewMode.Pets : PatientViewMode.Owners;

    /// <summary>Raised by the list/grid segmented switch.</summary>
    [RelayCommand]
    private void SetLayout(string? layout) =>
        Layout = layout == "grid" ? PatientLayout.Grid : PatientLayout.List;

    [RelayCommand]
    private void SetVaccineFilter(string? filter) => VaccineFilter = filter switch
    {
        "has" => VaccineFilter.Has,
        "none" => VaccineFilter.None,
        _ => VaccineFilter.All,
    };

    [RelayCommand]
    private void SetSort(string? sort) => Sort = sort switch
    {
        "name" => PatientSort.Name,
        "age" => PatientSort.Age,
        _ => PatientSort.Recent,
    };

    [RelayCommand]
    private void SetSpecies(string? species)
    {
        SelectedSpecies = string.IsNullOrEmpty(species) || species == "all"
            ? null
            : Enum.TryParse<PetSpecies>(species, ignoreCase: true, out var parsed)
                ? parsed
                : null;
        ApplyFilter();
    }

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    [RelayCommand]
    private void ResetFilters()
    {
        SearchText = string.Empty;
        SelectedSpecies = null;
        VaccineFilter = VaccineFilter.All;
        ApplyFilter();
    }

    /// <summary>
    /// Drill-in: opens the pet file in the centered record sheet (reference
    /// renders the patient file as <c>.sheet.pet-sheet</c> over the directory,
    /// not a separate page).
    /// </summary>
    [RelayCommand]
    private Task OpenPet(PetSummary? pet) =>
        pet is not null ? _presenter.OpenPetSheetAsync(pet.Id) : Task.CompletedTask;

    /// <summary>
    /// Drill-in: opens the owner file in the centered record sheet (reference
    /// <c>.sheet.owner-sheet</c>).
    /// </summary>
    [RelayCommand]
    private Task OpenOwner(OwnerSummary? owner) =>
        owner is not null ? _presenter.OpenOwnerSheetAsync(owner.Id) : Task.CompletedTask;

    [RelayCommand]
    private void ExportExcel() =>
        _toast.Show("خروجی اکسل آماده شد (نمونه طراحی)", Tone.Ok);

    /// <summary>Opens the add owner/pet wizard modal (Phase 09, 760-wide).</summary>
    [RelayCommand]
    private void AddNewOwnerPet() =>
        _dialogs.ShowModal(new AddPatientWizardViewModel(
            // The wizard persists a new owner/pet, so reload (which rebuilds the
            // lookup/haystack indexes) rather than re-filtering stale data.
            _patientsQueryService, _dialogs, () =>
            {
                _changes.Notify(DataDomain.Dashboard, DataDomain.Appointments, DataDomain.Visits);
                _ = LoadAsync();
            }), maxWidth: 760);

    [RelayCommand]
    private void CallOwner(OwnerSummary? owner)
    {
        if (owner is not null)
        {
            _toast.Show($"در حال برقراری تماس با {owner.Mobile1}", Tone.Info);
        }
    }

    /// <summary>Opens the add-pet modal for an existing owner (Phase 09, 760-wide).</summary>
    [RelayCommand]
    private async Task AddPetAsync(OwnerSummary? owner)
    {
        if (owner is null)
        {
            return;
        }

        var detail = await _patientsQueryService.GetOwnerAsync(owner.Id);
        if (detail is null)
        {
            return;
        }

        _dialogs.ShowModal(new AddPetModalViewModel(
            detail,
            async req =>
            {
                await _patientsQueryService.CreatePetAsync(detail.Id, req);
                _changes.Notify(DataDomain.Dashboard, DataDomain.Appointments, DataDomain.Visits);
                await LoadAsync();
                _toast.Show("حیوان جدید به پرونده مالک اضافه شد", Tone.Ok);
            },
            _dialogs), maxWidth: 760);
    }

    // Singletons must refresh when returning from another screen that edited these records.
    public override void OnNavigatedTo()
    {
        if (!IsBusy) _ = ReloadPageAsync();
    }

    private Task ReloadPageAsync() => TrackLoadResultAsync(LoadAsync);

    private readonly SemaphoreSlim _loadGate = new(1, 1);

    /// <returns>False when the load failed (already reported to the user here).</returns>
    private async Task<bool> LoadAsync()
    {
        await _loadGate.WaitAsync();
        IsBusy = true;
        try
        {
            // Publish only after BOTH complete. A failed reload must not mix new pets with old owners.
            var pets = await _patientsQueryService.GetPetsAsync();
            var owners = await _patientsQueryService.GetOwnersAsync();
            _allPets = pets;
            _allOwners = owners;
            RebuildIndexes();
            BuildSpeciesChips();
            ApplyFilter();
            return true;
        }
        catch (Exception)
        {
            _toast.Show("بارگذاری بیماران و مالکین انجام نشد؛ دوباره تلاش کنید", Tone.Danger);
            return false;
        }
        finally
        {
            IsBusy = false;
            _loadGate.Release();
        }
    }

    // Full, sorted match sets cached by ApplyFilter so page navigation only
    // re-slices (RematerializePage) instead of re-running the filter/sort.
    private IReadOnlyList<PetSummary> _sortedPetMatches = [];
    private IReadOnlyList<OwnerSummary> _sortedOwnerMatches = [];
    private ILookup<string, PetSummary> _matchedByOwner =
        Array.Empty<PetSummary>().ToLookup(p => p.OwnerId);

    private void ApplyFilter()
    {
        var needle = PersianText.Normalize(SearchText);
        var matchedPets = _allPets.Where(pet => PetMatches(pet, needle)).ToArray();
        _matchedByOwner = matchedPets.ToLookup(pet => pet.OwnerId);

        // Owner filtering mirrors the reference's ownerMatches: the owner's own
        // fields OR any of their pets matching, with species/vaccine filters
        // requiring at least one matching pet.
        var matchedOwners = _allOwners
            .Where(owner => OwnerMatches(owner, needle, _matchedByOwner.Contains(owner.Id)))
            .ToArray();

        // Sort the FULL match sets once; paging slices from these caches.
        _sortedPetMatches = SortPets(matchedPets).ToArray();
        _sortedOwnerMatches = SortOwners(matchedOwners).ToArray();
        _totalPetMatches = _sortedPetMatches.Count;
        _totalOwnerMatches = _sortedOwnerMatches.Count;

        // A filter/sort/view change always returns to the first page.
        CurrentPage = 1;
        RematerializePage();

        foreach (var chip in SpeciesChips) chip.IsActive = chip.Value == SelectedSpecies;
    }

    /// <summary>
    /// Materializes only the current page's slice of the cached match sets into
    /// the bound collections (card VMs are created here, so off-page records
    /// never allocate a VM — the core first-paint win). Called on every page or
    /// page-size change; the expensive filter/sort stays in ApplyFilter.
    /// </summary>
    private void RematerializePage()
    {
        // Active entity drives the page total used for clamping.
        _totalMatchCount = IsOwnersView ? _totalOwnerMatches : _totalPetMatches;
        CurrentPage = Math.Clamp(CurrentPage, 1, Math.Max(1, PageCount));

        var skip = ShowAll ? 0 : (CurrentPage - 1) * PageSize;
        var take = ShowAll ? int.MaxValue : PageSize;

        var pagePets = _sortedPetMatches.Skip(skip).Take(take);
        var pageOwners = _sortedOwnerMatches.Skip(skip).Take(take);

        FilteredPets.ReplaceAll(
            pagePets.Select(pet => new PetCardViewModel(pet, OwnerById(pet.OwnerId))));

        FilteredOwners.ReplaceAll(
            pageOwners.Select(owner => new OwnerCardViewModel(
                owner,
                PetsOfOwner(owner.Id),
                _matchedByOwner[owner.Id].ToArray())));

        // Virtualize rows, not individual cards, exactly like the boarding directory.
        OwnerRows.ReplaceAll(FilteredOwners.Chunk(3).Select(cards => new OwnerCardRowViewModel(cards)));
        PetRows.ReplaceAll(FilteredPets.Chunk(3).Select(cards => new PetCardRowViewModel(cards)));

        NotifyDerived();
    }

    private bool PetMatches(PetSummary pet, string needle)
    {
        if (SelectedSpecies is { } species && pet.Species != species)
        {
            return false;
        }

        if (VaccineFilter == VaccineFilter.Has && !pet.HasVaccines)
        {
            return false;
        }

        if (VaccineFilter == VaccineFilter.None && pet.HasVaccines)
        {
            return false;
        }

        if (needle.Length == 0)
        {
            return true;
        }

        // Text match uses the pre-normalized haystack built in RebuildIndexes()
        // instead of re-joining/normalizing per keystroke (PERFORMANCE.md §2).
        return _petHaystack.TryGetValue(pet.Id, out var haystack)
            && PersianText.Contains(haystack, needle);
    }

    private bool OwnerMatches(OwnerSummary owner, string needle, bool hasPet)
    {

        // A species/vaccination filter narrows owners to those with a match.
        if (SelectedSpecies is not null || VaccineFilter != VaccineFilter.All)
        {
            return hasPet;
        }

        if (needle.Length == 0)
        {
            return true;
        }

        // Text match uses the pre-normalized haystack built in RebuildIndexes()
        // instead of re-joining/normalizing per keystroke (PERFORMANCE.md §2).
        return (_ownerHaystack.TryGetValue(owner.Id, out var haystack)
            && PersianText.Contains(haystack, needle)) || hasPet;
    }

    /// <summary>
    /// Sorts pets per the reference's <c>sortPets</c>: name = A–Z (Persian),
    /// age = oldest first, recent = newest last-visit first (missing = 1390/01/01).
    /// </summary>
    private IEnumerable<PetSummary> SortPets(IEnumerable<PetSummary> pets)
    {
        var list = pets.ToList();
        switch (Sort)
        {
            case PatientSort.Name:
                list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCulture));
                break;
            case PatientSort.Age:
                list.Sort((a, b) => a.BirthDate.CompareTo(b.BirthDate));
                break;
            default:
                list.Sort((a, b) =>
                    (b.LastVisitDate ?? new DateTime(2011, 3, 21)).CompareTo(
                        a.LastVisitDate ?? new DateTime(2011, 3, 21)));
                break;
        }

        return list;
    }

    private static readonly StringComparer PersianNameComparer =
        StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("fa-IR"), ignoreCase: true);

    private IEnumerable<OwnerSummary> SortOwners(IEnumerable<OwnerSummary> owners) => Sort switch
    {
        PatientSort.Name => owners.OrderBy(o => PersianText.Normalize($"{o.FirstName} {o.LastName}"), PersianNameComparer)
            .ThenBy(o => o.Id, StringComparer.Ordinal),
        PatientSort.Age => owners.OrderBy(o => o.Since).ThenBy(o => o.Id, StringComparer.Ordinal),
        _ => owners.OrderByDescending(o => o.Since).ThenByDescending(o => o.Id, StringComparer.Ordinal),
    };

    private void BuildSpeciesChips()
    {
        SpeciesChips.Clear();
        SpeciesChips.Add(new PatientsSpeciesChipViewModel(
            null, "همه گونه‌ها", "IconPaw", _allPets.Count));

        foreach (var species in new[]
                 {
                     PetSpecies.Dog, PetSpecies.Cat, PetSpecies.Bird,
                     PetSpecies.Rabbit, PetSpecies.Rodent, PetSpecies.Other,
                 })
        {
            var count = _allPets.Count(p => p.Species == species);
            if (count == 0)
            {
                continue;
            }

            SpeciesChips.Add(new PatientsSpeciesChipViewModel(
                species,
                PatientsPresentation.SpeciesLabel(species),
                PatientsPresentation.SpeciesIconKey(species),
                count));
        }

        foreach (var chip in SpeciesChips)
        {
            chip.IsActive = chip.Value == SelectedSpecies;
        }
    }

    private void NotifyDerived()
    {
        OnPropertyChanged(nameof(HasOwners));
        OnPropertyChanged(nameof(HasPets));
        OnPropertyChanged(nameof(HasSearchText));
        OnPropertyChanged(nameof(ResultCountText));

        // Entity / layout switches — the XAML binds visibility to these computed
        // properties, so they must be re-raised whenever ViewMode/Layout or the
        // filtered collections change; otherwise the toggle only takes effect
        // after navigating away and back (forcing a full re-render).
        OnPropertyChanged(nameof(IsOwnersView));
        OnPropertyChanged(nameof(RecentSortLabel));
        OnPropertyChanged(nameof(AgeSortLabel));
        OnPropertyChanged(nameof(IsPetsView));
        OnPropertyChanged(nameof(IsGridView));
        OnPropertyChanged(nameof(IsListView));

        // The four render combinations + shared empty state.
        OnPropertyChanged(nameof(ShowOwnersGrid));
        OnPropertyChanged(nameof(ShowOwnersTable));
        OnPropertyChanged(nameof(ShowPetsGrid));
        OnPropertyChanged(nameof(ShowPetsTable));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(EmptyTitle));

        NotifyPaginationDerived();
        ClearSearchCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Re-raises the pagination-derived properties and nav CanExecute.</summary>
    private void NotifyPaginationDerived()
    {
        OnPropertyChanged(nameof(PageCount));
        OnPropertyChanged(nameof(ShowPagination));
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(PageIndicatorText));
        OnPropertyChanged(nameof(PageRangeText));
        NextPageCommand.NotifyCanExecuteChanged();
        PreviousPageCommand.NotifyCanExecuteChanged();
    }

    private OwnerSummary? OwnerById(string ownerId) =>
        _ownersById.GetValueOrDefault(ownerId);

    private IReadOnlyList<PetSummary> PetsOfOwner(string ownerId) =>
        _petsByOwnerId.GetValueOrDefault(ownerId) ?? [];

    /// <summary>
    /// Rebuilds the owner/pet id indexes and the per-item search haystacks.
    /// Call once whenever <see cref="_allPets"/>/<see cref="_allOwners"/> change
    /// (i.e. after every <see cref="LoadAsync"/>) — never inside the filter loop.
    /// Mirrors <c>AppointmentsCalendarViewModel.RebuildIndexes()</c>. This is what
    /// keeps <see cref="ApplyFilter"/> at O(owners + pets) instead of O(owners × pets).
    /// </summary>
    private void RebuildIndexes()
    {
        _ownersById.Clear();
        _ownerHaystack.Clear();
        foreach (var owner in _allOwners)
        {
            _ownersById[owner.Id] = owner;
            _ownerHaystack[owner.Id] = PersianText.Normalize(string.Join(' ',
                owner.FirstName, owner.LastName, owner.File, owner.Mobile1, owner.Mobile2,
                owner.Phone, owner.NationalId, owner.Address, owner.Referral));
        }

        _petsByOwnerId.Clear();
        _petHaystack.Clear();
        foreach (var pet in _allPets)
        {
            if (!_petsByOwnerId.TryGetValue(pet.OwnerId, out var bucket))
            {
                bucket = [];
                _petsByOwnerId[pet.OwnerId] = bucket;
            }

            bucket.Add(pet);

            var owner = _ownersById.GetValueOrDefault(pet.OwnerId);
            _petHaystack[pet.Id] = PersianText.Normalize(string.Join(' ',
                pet.Name, pet.Code, pet.Breed, PatientsPresentation.SpeciesLabel(pet.Species),
                pet.Color, owner?.FirstName, owner?.LastName, owner?.File,
                owner?.Mobile1, owner?.Mobile2, owner?.Phone, owner?.NationalId, pet.MicrochipCode));
        }
    }
}
