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
    private readonly IToastService _toast;
    private IReadOnlyList<PetSummary> _allPets = [];
    private IReadOnlyList<OwnerSummary> _allOwners = [];

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

    public PatientsPageViewModel(IPatientsQueryService patientsQueryService, IToastService toast)
    {
        _patientsQueryService = patientsQueryService;
        _toast = toast;
        Title = "بیماران و مالکین";
        Eyebrow = "مدیریت کلینیک";
        Breadcrumbs.Add("مدیریت کلینیک");
        Breadcrumbs.Add("بیماران");
        LoadAsync().GetAwaiter().GetResult();
    }

    public ObservableCollection<OwnerCardViewModel> FilteredOwners { get; } = [];
    public ObservableCollection<PetCardViewModel> FilteredPets { get; } = [];
    public ObservableCollection<PatientsSpeciesChipViewModel> SpeciesChips { get; } = [];

    public bool IsOwnersView => ViewMode == PatientViewMode.Owners;
    public bool IsPetsView => ViewMode == PatientViewMode.Pets;
    public bool IsGridView => Layout == PatientLayout.Grid;
    public bool IsListView => Layout == PatientLayout.List;
    public bool HasOwners => FilteredOwners.Count > 0;
    public bool HasPets => FilteredPets.Count > 0;
    public bool HasSearchText => !string.IsNullOrWhiteSpace(SearchText);

    // Content-region visibility: the four rendering combinations plus the
    // shared empty state (reference render() swaps host class + content).
    public bool ShowOwnersGrid => IsOwnersView && IsGridView && HasOwners;
    public bool ShowOwnersTable => IsOwnersView && IsListView && HasOwners;
    public bool ShowPetsGrid => IsPetsView && IsGridView && HasPets;
    public bool ShowPetsTable => IsPetsView && IsListView && HasPets;
    public bool ShowEmptyState => (IsOwnersView && !HasOwners) || (IsPetsView && !HasPets);
    public string EmptyTitle => IsOwnersView ? "مالکی پیدا نشد" : "بیماری پیدا نشد";

    /// <summary>Result counter text (reference toolbar .pb-count).</summary>
    public string ResultCountText => ViewMode == PatientViewMode.Owners
        ? $"{PersianNumberFormatter.ToPersianDigits(FilteredOwners.Count)} مالک · "
          + $"{PersianNumberFormatter.ToPersianDigits(FilteredPets.Count)} بیمار"
        : $"{PersianNumberFormatter.ToPersianDigits(FilteredPets.Count)} بیمار";

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

    partial void OnSearchTextChanged(string value) => ApplyFilter();

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

    /// <summary>Drill-in stub — Phase 09 replaces this with the pet-file view.</summary>
    [RelayCommand]
    private void OpenPet(PetSummary? pet)
    {
        if (pet is not null)
        {
            _toast.Show($"پرونده بیمار «{pet.Name}» — فاز ۰۹", Tone.Info);
        }
    }

    /// <summary>Drill-in stub — Phase 09 replaces this with the owner-sheet view.</summary>
    [RelayCommand]
    private void OpenOwner(OwnerSummary? owner)
    {
        if (owner is not null)
        {
            _toast.Show($"پرونده مالک «{owner.FirstName} {owner.LastName}» — فاز ۰۹", Tone.Info);
        }
    }

    [RelayCommand]
    private void ExportExcel() =>
        _toast.Show("خروجی اکسل آماده شد (نمونه طراحی)", Tone.Ok);

    /// <summary>Add-flow stub — Phase 09 owns the owner/pet wizard.</summary>
    [RelayCommand]
    private void AddNewOwnerPet() =>
        _toast.Show("ثبت مالک و حیوان جدید — فاز ۰۹", Tone.Info);

    [RelayCommand]
    private void CallOwner(OwnerSummary? owner)
    {
        if (owner is not null)
        {
            _toast.Show($"در حال برقراری تماس با {owner.Mobile1}", Tone.Info);
        }
    }

    /// <summary>Add-pet-to-existing-owner stub — Phase 09 owns the pet modal.</summary>
    [RelayCommand]
    private void AddPet(OwnerSummary? owner)
    {
        if (owner is not null)
        {
            _toast.Show($"افزودن حیوان به پرونده «{owner.FirstName} {owner.LastName}» — فاز ۰۹", Tone.Info);
        }
    }

    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            _allPets = await _patientsQueryService.GetPetsAsync();
            _allOwners = await _patientsQueryService.GetOwnersAsync();
            ApplyFilter();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyFilter()
    {
        var needle = PersianText.Normalize(SearchText);
        var matchedPets = _allPets.Where(pet => PetMatches(pet, needle)).ToArray();

        // Owner filtering mirrors the reference's ownerMatches: the owner's own
        // fields OR any of their pets matching, with species/vaccine filters
        // requiring at least one matching pet.
        var matchedOwners = _allOwners
            .Where(owner => OwnerMatches(owner, needle, matchedPets))
            .ToArray();

        FilteredPets.Clear();
        foreach (var pet in SortPets(matchedPets))
        {
            FilteredPets.Add(new PetCardViewModel(pet, OwnerById(pet.OwnerId)));
        }

        FilteredOwners.Clear();
        foreach (var owner in matchedOwners)
        {
            var pets = PetsOfOwner(owner.Id);
            FilteredOwners.Add(new OwnerCardViewModel(owner, pets, matchedPets.Where(p => p.OwnerId == owner.Id).ToArray()));
        }

        BuildSpeciesChips();
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

        var owner = OwnerById(pet.OwnerId);
        var haystack = PersianText.Normalize(string.Join(' ',
            pet.Name, pet.Code, pet.Breed, PatientsPresentation.SpeciesLabel(pet.Species),
            pet.Color, owner?.FirstName, owner?.LastName, owner?.File,
            owner?.Mobile1, owner?.Mobile2, owner?.Phone, owner?.NationalId, pet.MicrochipCode));
        return PersianText.Contains(haystack, needle);
    }

    private bool OwnerMatches(OwnerSummary owner, string needle, IReadOnlyList<PetSummary> matchedPets)
    {
        var hasPet = matchedPets.Any(p => p.OwnerId == owner.Id);

        // A species/vaccination filter narrows owners to those with a match.
        if (SelectedSpecies is not null || VaccineFilter != VaccineFilter.All)
        {
            return hasPet;
        }

        if (needle.Length == 0)
        {
            return true;
        }

        var haystack = PersianText.Normalize(string.Join(' ',
            owner.FirstName, owner.LastName, owner.File, owner.Mobile1, owner.Mobile2,
            owner.Phone, owner.NationalId, owner.Address, owner.Referral));
        return PersianText.Contains(haystack, needle) || hasPet;
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
        ClearSearchCommand.NotifyCanExecuteChanged();
    }

    private OwnerSummary? OwnerById(string ownerId) =>
        _allOwners.FirstOrDefault(o => o.Id == ownerId);

    private IReadOnlyList<PetSummary> PetsOfOwner(string ownerId) =>
        _allPets.Where(p => p.OwnerId == ownerId).ToArray();
}
