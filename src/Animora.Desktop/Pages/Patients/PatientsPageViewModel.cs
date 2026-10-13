using Animora.Contracts.Diagnostics;
using Animora.Contracts.Patients;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Pages.Patients.Settings;
using Animora.Desktop.Services;
using Animora.Desktop.Shell;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Patients;

/// <summary>
/// ViewModel for the Patients &amp; Owners directory (Phase 08, C01 scalability pass).
/// <para>
/// Search, species/vaccine filtering, sorting and paging all run <b>server-side</b>: every toolbar
/// change rebuilds a <see cref="PatientsDirectoryQuery"/> and asks <see cref="IPatientsQueryService"/>
/// for exactly one bounded page of the active entity plus the directory facet counts. The page never
/// materializes the whole table in RAM, never loops over every DB page, and does no client-side
/// filter/sort/haystack work — so it scales to 50k+ records with a constant memory + 2-query cost.
/// </para>
/// </summary>
public sealed partial class PatientsPageViewModel : PageViewModelBase
{
    private readonly IPatientsQueryService _patientsQueryService;
    private readonly IRecordSheetPresenter _presenter;
    private readonly IDialogService _dialogs;
    private readonly IToastService _toast;
    private readonly IDataChangeNotifier _changes;

    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>The active page-level section: the records directory or the settings tab.</summary>
    [ObservableProperty]
    private PatientsSection _section = PatientsSection.Directory;

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

    /// <summary>Selectable page sizes shown in the toolbar dropdown. "همه" maps to the server page
    /// ceiling (200) — the directory is always paged now, so there is no unbounded "show all".</summary>
    public IReadOnlyList<PatientsPageSizeOption> PageSizeOptions { get; } =
    [
        new(25, "۲۵ پرونده"),
        new(50, "۵۰ پرونده"),
        new(100, "۱۰۰ پرونده"),
        new(ServerMaxPageSize, "همه"),
    ];

    // The server hard-caps a page at 200 rows (QueryPaging.HardMaxPageSize); mirror it here so the
    // page math and the requested page size never disagree with what the DB can return.
    private const int ServerMaxPageSize = 200;

    // Default 50 (index 1) — the requested default visible count.
    [ObservableProperty]
    private PatientsPageSizeOption _selectedPageSize = new(50, "۵۰ پرونده");

    [ObservableProperty]
    private int _currentPage = 1;

    // Facet counters for the active query (resolved server-side alongside the page).
    private int _totalOwnerMatches;
    private int _totalPetMatches;
    private bool _hasLoadedOnce;

    // Total matches for the ACTIVE entity (owners or pets) — drives the page math + nav state.
    private int _totalMatchCount;

    private readonly IStoreReadinessGate _readiness;

    public PatientsPageViewModel(
        IPatientsQueryService patientsQueryService,
        IStoreReadinessGate readiness,
        IRecordSheetPresenter presenter,
        IDialogService dialogs,
        IToastService toast,
        IDataChangeNotifier changes)
    {
        _patientsQueryService = patientsQueryService;
        _readiness = readiness;
        _changes = changes;
        _presenter = presenter;
        _dialogs = dialogs;
        _toast = toast;
        Settings = new PatientsSettingsViewModel(toast);
        Title = "بیماران و سرپرستان";
        Eyebrow = "مدیریت کلینیک";
        Breadcrumbs.Add("مدیریت کلینیک");
        Breadcrumbs.Add("بیماران");
        // Non-blocking init (PERFORMANCE.md §4): never block the UI thread on the first load.
        UseLoadStatus(ReloadPageAsync);
        // Owner/pet sheets edit records from over this page; the page's own wizard/add-pet writes reload directly.
        RefreshOnDataChange(changes, () => _ = ReloadPageAsync(), DataDomain.Patients);
        _ = ReloadPageAsync();
        _hasLoadedOnce = true;   // the ctor-started load covers the first navigation (PERFORMANCE.md §13)
    }

    /// <summary>The settings tab's ViewModel (species &amp; breeds, SMS toggle).</summary>
    public PatientsSettingsViewModel Settings { get; }

    public RangeObservableCollection<OwnerCardViewModel> FilteredOwners { get; } = [];
    public RangeObservableCollection<PetCardViewModel> FilteredPets { get; } = [];
    public RangeObservableCollection<OwnerCardRowViewModel> OwnerRows { get; } = [];
    public RangeObservableCollection<PetCardRowViewModel> PetRows { get; } = [];
    public RangeObservableCollection<PatientsSpeciesChipViewModel> SpeciesChips { get; } = [];

    // Page-level section: records directory vs. settings tab.
    public bool IsDirectorySection => Section == PatientsSection.Directory;
    public bool IsSettingsSection => Section == PatientsSection.Settings;

    public bool IsOwnersView => ViewMode == PatientViewMode.Owners;
    public bool IsPetsView => ViewMode == PatientViewMode.Pets;

    // The three section tabs (سرپرستان / بیماران / تنظیمات) are mutually exclusive: the owners/pets
    // tabs only light up while the directory is showing, otherwise «تنظیمات» is the active tab.
    public bool IsOwnersTabActive => IsDirectorySection && IsOwnersView;
    public bool IsPetsTabActive => IsDirectorySection && IsPetsView;
    public bool IsGridView => Layout == PatientLayout.Grid;
    public bool IsListView => Layout == PatientLayout.List;
    public bool HasOwners => _totalOwnerMatches > 0;
    public bool HasPets => _totalPetMatches > 0;
    public bool HasSearchText => !string.IsNullOrWhiteSpace(SearchText);
    public string RecentSortLabel => IsOwnersView ? "جدیدترین ثبت" : "جدیدترین ویزیت";
    public string AgeSortLabel => IsOwnersView ? "قدیمی‌ترین ثبت" : "سن";

    // Content-region visibility: the four rendering combinations plus the shared empty state.
    public bool ShowOwnersGrid => IsOwnersView && IsGridView && HasOwners;
    public bool ShowOwnersTable => IsOwnersView && IsListView && HasOwners;
    public bool ShowPetsGrid => IsPetsView && IsGridView && HasPets;
    public bool ShowPetsTable => IsPetsView && IsListView && HasPets;
    public bool ShowEmptyState => (IsOwnersView && !HasOwners) || (IsPetsView && !HasPets);
    public string EmptyTitle => IsOwnersView ? "سرپرستی پیدا نشد" : "بیماری پیدا نشد";

    // ── Pagination-derived state ──────────────────────────────────────────
    private int PageSize => Math.Clamp(SelectedPageSize?.Value ?? 50, 1, ServerMaxPageSize);

    /// <summary>Total number of pages for the active entity (>= 1).</summary>
    public int PageCount => _totalMatchCount == 0
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

    /// <summary>Range caption, e.g. «۵۱–۱۰۰ از ۲۴۰».</summary>
    public string PageRangeText
    {
        get
        {
            if (_totalMatchCount == 0)
            {
                return string.Empty;
            }

            var from = ((CurrentPage - 1) * PageSize) + 1;
            var to = Math.Min(CurrentPage * PageSize, _totalMatchCount);
            return $"{PersianNumberFormatter.ToPersianDigits(from)}–{PersianNumberFormatter.ToPersianDigits(to)}"
                 + $" از {PersianNumberFormatter.ToPersianDigits(_totalMatchCount)}";
        }
    }

    /// <summary>Result counter text (reference toolbar .pb-count).</summary>
    public string ResultCountText => ViewMode == PatientViewMode.Owners
        ? $"{PersianNumberFormatter.ToPersianDigits(_totalOwnerMatches)} سرپرست · "
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

    private readonly Debouncer _searchDebounce = new(TimeSpan.FromMilliseconds(220));

    // Typing re-queries the server; debounced so a burst of keystrokes is one DB round-trip.
    partial void OnSearchTextChanged(string value) =>
        _searchDebounce.Debounce(() => ReloadFromFilter());

    partial void OnSectionChanged(PatientsSection value)
    {
        OnPropertyChanged(nameof(IsDirectorySection));
        OnPropertyChanged(nameof(IsSettingsSection));
        OnPropertyChanged(nameof(IsOwnersTabActive));
        OnPropertyChanged(nameof(IsPetsTabActive));
    }

    partial void OnViewModeChanged(PatientViewMode value)
    {
        OnPropertyChanged(nameof(IsOwnersTabActive));
        OnPropertyChanged(nameof(IsPetsTabActive));
        ReloadFromFilter();
    }

    partial void OnLayoutChanged(PatientLayout value) => NotifyDerived();

    partial void OnVaccineFilterChanged(VaccineFilter value)
    {
        OnPropertyChanged(nameof(VaccineFilterIndex));
        ReloadFromFilter();
    }

    partial void OnSortChanged(PatientSort value)
    {
        OnPropertyChanged(nameof(SortIndex));
        ReloadFromFilter();
    }

    partial void OnSelectedSpeciesChanged(PetSpecies? value) => ReloadFromFilter();

    // Changing page size resets to page 1 and re-queries.
    partial void OnSelectedPageSizeChanged(PatientsPageSizeOption value) => ReloadFromFilter();

    partial void OnCurrentPageChanged(int value) => NotifyPaginationDerived();

    /// <summary>Go to the next page of results (toolbar/footer nav).</summary>
    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void NextPage()
    {
        if (CanGoNext)
        {
            CurrentPage++;
            _ = ReloadPageAsync();
        }
    }

    /// <summary>Go to the previous page of results.</summary>
    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private void PreviousPage()
    {
        if (CanGoPrevious)
        {
            CurrentPage--;
            _ = ReloadPageAsync();
        }
    }

    /// <summary>Raised by the page-level section switch (directory / settings).</summary>
    [RelayCommand]
    private void SetSection(string? section) =>
        Section = section == "settings" ? PatientsSection.Settings : PatientsSection.Directory;

    [RelayCommand]
    private void ToggleViewMode() =>
        ViewMode = ViewMode == PatientViewMode.Owners ? PatientViewMode.Pets : PatientViewMode.Owners;

    /// <summary>Raised by the toolbar's owners/pets segmented switch.</summary>
    [RelayCommand]
    private void SetViewMode(string? mode)
    {
        // Picking owners/pets from the settings tab returns to the directory.
        Section = PatientsSection.Directory;
        ViewMode = mode == "pets" ? PatientViewMode.Pets : PatientViewMode.Owners;
    }

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
    private void SetSpecies(string? species) =>
        SelectedSpecies = string.IsNullOrEmpty(species) || species == "all"
            ? null
            : Enum.TryParse<PetSpecies>(species, ignoreCase: true, out var parsed)
                ? parsed
                : null;

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    [RelayCommand]
    private void ResetFilters()
    {
        // Suppress the per-property reloads so clearing three filters is a single round-trip.
        _suppressReload = true;
        SearchText = string.Empty;
        SelectedSpecies = null;
        VaccineFilter = VaccineFilter.All;
        _suppressReload = false;
        ReloadFromFilter();
    }

    /// <summary>
    /// Drill-in: opens the pet file in the centered record sheet.
    /// </summary>
    [RelayCommand]
    private Task OpenPet(PetSummary? pet) =>
        pet is not null ? _presenter.OpenPetSheetAsync(pet.Id) : Task.CompletedTask;

    /// <summary>
    /// Drill-in: opens the owner file in the centered record sheet.
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
            _patientsQueryService, _dialogs, () =>
            {
                _changes.Notify(DataDomain.Dashboard, DataDomain.Appointments, DataDomain.Visits);
                _ = ReloadPageAsync();
            }, _toast), maxWidth: 760);

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

        OwnerDetail? detail;
        try
        {
            detail = await _patientsQueryService.GetOwnerAsync(owner.Id);
        }
        catch (InvalidOperationException)
        {
            _toast.Show(AccessLostMessage, Tone.Danger);
            return;
        }

        if (detail is null)
        {
            _toast.Show("سرپرست پیدا نشد", Tone.Info);
            return;
        }

        _dialogs.ShowModal(new AddPetModalViewModel(
            detail,
            async req =>
            {
                await _patientsQueryService.CreatePetAsync(detail.Id, req);
                _changes.Notify(DataDomain.Dashboard, DataDomain.Appointments, DataDomain.Visits);
                await ReloadPageAsync();
                _toast.Show("حیوان جدید به پرونده سرپرست اضافه شد", Tone.Ok);
            },
            _dialogs, _toast), maxWidth: 760);
    }

    // Singletons must refresh when returning from another screen that edited these records.
    public override void OnNavigatedTo()
    {
        // The first navigation is already covered by the load the ctor started — skip it so first entry
        // does not queue a duplicate load behind the _loadGate. Later visits refresh as before.
        if (_hasLoadedOnce)
        {
            _hasLoadedOnce = false;
            return;
        }

        // Queue a fresh read even if an earlier visit is still loading; its revision is now obsolete.
        _ = ReloadPageAsync();
    }

    // F-05: this page's ViewModel is a singleton, so its result collections would otherwise stay resident
    // for the whole app lifetime. Release the large, per-visit result lists when leaving; OnNavigatedTo
    // always re-queries on the next visit, so returning repopulates them.
    public override void OnNavigatedFrom()
    {
        _loadRevision++; // In-flight reads from this visit must not repopulate released collections.
        FilteredOwners.Clear();
        FilteredPets.Clear();
        OwnerRows.Clear();
        PetRows.Clear();
    }

    private long _loadRevision;

    private Task ReloadPageAsync()
    {
        var revision = ++_loadRevision;
        return TrackLoadResultAsync(() => LoadAsync(revision));
    }

    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private bool _suppressReload;

    /// <summary>A filter/sort/view/page-size change: snap back to page 1 and re-query the server.</summary>
    private void ReloadFromFilter()
    {
        if (_suppressReload) return;
        CurrentPage = 1;
        _ = ReloadPageAsync();
    }

    /// <summary>Builds the current directory query from the toolbar state.</summary>
    private PatientsDirectoryQuery CurrentQuery() => new(
        Search: SearchText,
        Species: SelectedSpecies,
        Vaccine: VaccineFilter,
        Sort: MapSort(Sort),
        Page: CurrentPage,
        PageSize: PageSize);

    private static PatientsSortOrder MapSort(PatientSort sort) => sort switch
    {
        PatientSort.Name => PatientsSortOrder.Name,
        PatientSort.Age => PatientsSortOrder.Age,
        _ => PatientsSortOrder.Recent,
    };

    // Delegate to the shared helpers (PatientsPresentation) so the wizard, add-pet modal and owner sheet
    // all classify command failures the same way.
    private const string AccessLostMessage = PatientsPresentation.AccessLostMessage;

    /// <summary>Permission / session / capability failures (as opposed to a store or code fault).</summary>
    private static bool IsAccessFailure(string code) => PatientsPresentation.IsAccessFailure(code);

    /// <returns>False when the load failed (already reported to the user here).</returns>
    private async Task<bool> LoadAsync(long revision)
    {
        await _loadGate.WaitAsync();
        if (revision != _loadRevision)
        {
            _loadGate.Release();
            return true;
        }
        IsBusy = true;
        try
        {
            // Wait for the local store (migrations/seed) before the first query — same as every Track C page.
            await _readiness.WaitAsync();
            if (revision != _loadRevision) return true;
            var query = CurrentQuery();
            var ownersView = IsOwnersView;

            // Capture the query and entity mode together. A newer filter/navigation request supersedes
            // this read; never publish an old owners response into the pets view (or a departed visit).
            var facetsTask = _patientsQueryService.GetDirectoryFacetsAsync(query);
            if (ownersView)
            {
                var pageTask = _patientsQueryService.GetOwnersPageAsync(query);
                await Task.WhenAll(facetsTask, pageTask);
                var page = await pageTask;
                var validPage = ClampPage(query, page.TotalCount);
                if (validPage != query.Page)
                {
                    query = query with { Page = validPage };
                    page = await _patientsQueryService.GetOwnersPageAsync(query);
                }
                if (revision != _loadRevision) return true;
                CurrentPage = query.Page;
                PublishFacets((await facetsTask) with { TotalOwners = page.TotalCount });
                PublishOwners(page);
                FilteredPets.Clear();
                PetRows.Clear();
            }
            else
            {
                var pageTask = _patientsQueryService.GetPetsPageAsync(query);
                await Task.WhenAll(facetsTask, pageTask);
                var page = await pageTask;
                var validPage = ClampPage(query, page.TotalCount);
                if (validPage != query.Page)
                {
                    query = query with { Page = validPage };
                    page = await _patientsQueryService.GetPetsPageAsync(query);
                }
                if (revision != _loadRevision) return true;
                CurrentPage = query.Page;
                PublishFacets((await facetsTask) with { TotalPets = page.TotalCount });
                PublishPets(page);
                FilteredOwners.Clear();
                OwnerRows.Clear();
            }

            NotifyDerived();
            return true;
        }
        catch (InvalidOperationException ex) when (IsAccessFailure(ex.Message))
        {
            if (revision != _loadRevision) return true;
            _toast.Show(AccessLostMessage, Tone.Danger);
            return false;
        }
        catch (Exception)
        {
            if (revision != _loadRevision) return true;
            _toast.Show("بارگذاری بیماران و سرپرستان انجام نشد؛ دوباره تلاش کنید", Tone.Danger);
            return false;
        }
        finally
        {
            IsBusy = false;
            _loadGate.Release();
        }
    }

    private static int ClampPage(PatientsDirectoryQuery query, int totalCount) =>
        Math.Clamp(query.Page, 1, Math.Max(1, (int)Math.Ceiling(totalCount / (double)query.PageSize)));

    private void PublishFacets(PatientsDirectoryFacets facets)
    {
        _totalOwnerMatches = facets.TotalOwners;
        _totalPetMatches = facets.TotalPets;
        _totalMatchCount = IsOwnersView ? _totalOwnerMatches : _totalPetMatches;

        BuildSpeciesChips(facets.SpeciesCounts);
    }

    private void PublishOwners(PatientsPage<OwnerSummary> page)
    {
        // Server-side owner paging: no pet roster in RAM. OwnerCard renders from OwnerSummary
        // (PetCount is SQL-projected); the pet chips degrade gracefully (see OwnerCardViewModel).
        FilteredOwners.ReplaceAll(page.Items.Select(owner => new OwnerCardViewModel(owner)));
        OwnerRows.ReplaceAll(FilteredOwners.Chunk(3).Select(cards => new OwnerCardRowViewModel(cards)));
    }

    private void PublishPets(PatientsPage<PetSummary> page)
    {
        FilteredPets.ReplaceAll(page.Items.Select(pet => new PetCardViewModel(pet, owner: null)));
        PetRows.ReplaceAll(FilteredPets.Chunk(3).Select(cards => new PetCardRowViewModel(cards)));
    }

    private void BuildSpeciesChips(IReadOnlyDictionary<PetSpecies, int> speciesCounts)
    {
        SpeciesChips.Clear();
        SpeciesChips.Add(new PatientsSpeciesChipViewModel(
            null, "همه گونه‌ها", "IconPaw", _totalPetMatches));

        foreach (var species in new[]
                 {
                     PetSpecies.Dog, PetSpecies.Cat, PetSpecies.Bird,
                     PetSpecies.Rabbit, PetSpecies.Rodent, PetSpecies.Reptile, PetSpecies.Other,
                 })
        {
            var count = speciesCounts.GetValueOrDefault(species);
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

        OnPropertyChanged(nameof(IsOwnersView));
        OnPropertyChanged(nameof(IsOwnersTabActive));
        OnPropertyChanged(nameof(IsPetsTabActive));
        OnPropertyChanged(nameof(RecentSortLabel));
        OnPropertyChanged(nameof(AgeSortLabel));
        OnPropertyChanged(nameof(IsPetsView));
        OnPropertyChanged(nameof(IsGridView));
        OnPropertyChanged(nameof(IsListView));

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
}
