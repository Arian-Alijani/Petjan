using System.Collections.ObjectModel;
using Animora.Contracts.Patients;
using Animora.Desktop.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Patients;

/// <summary>One species tile in a wizard's type picker (.type-tile).</summary>
public sealed partial class SpeciesTileViewModel : ObservableObject
{
    public SpeciesTileViewModel(PetSpecies species)
    {
        Species = species;
        Label = PatientsPresentation.SpeciesLabel(species);
        IconKey = PatientsPresentation.SpeciesIconKey(species);
    }

    public PetSpecies Species { get; }
    public string Label { get; }
    public string IconKey { get; }

    [ObservableProperty] private bool _isActive;
}

/// <summary>
/// Reusable pet-fields model shared by the add-owner wizard and the add-pet
/// modal (the reference's #wizTypes/#petTypes + the pet field grids). Mirrors
/// <c>bindTypePicker</c>/<c>fillOptions</c> — selecting a species swaps the
/// breed select, and every field maps to a plain property the View binds.
/// </summary>
public sealed partial class PetFormViewModel : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _color = string.Empty;
    [ObservableProperty] private string _birth = string.Empty;
    [ObservableProperty] private string _habitat = string.Empty;
    [ObservableProperty] private string _behavior = string.Empty;
    [ObservableProperty] private string _diet = string.Empty;
    [ObservableProperty] private string _surgeryHistory = string.Empty;
    [ObservableProperty] private string _microchipCode = string.Empty;
    [ObservableProperty] private string _microchipDate = string.Empty;
    [ObservableProperty] private string _microchipVendor = string.Empty;
    [ObservableProperty] private string _microchipLocation = string.Empty;
    [ObservableProperty] private string _bloodType = string.Empty;
    [ObservableProperty] private string _insurance = string.Empty;
    [ObservableProperty] private string _neuteredDate = string.Empty;
    [ObservableProperty] private bool _neutered;

    /// <summary>
    /// Whether the "نوع حیوان" / "میکروچیپ و وضعیت" section-title rules are
    /// shown. True in the owner wizard (its step-2 pane draws the titled section
    /// headers), false in the compact add-pet modal, which renders the same
    /// fields without the extra titles (reference #petTypes block).
    /// </summary>
    [ObservableProperty] private bool _showSectionTitles = true;

    [ObservableProperty] private bool _isMale = true;
    [ObservableProperty] private bool _isFemale;
    [ObservableProperty] private bool _isUnknown;
    [ObservableProperty] private PetSpecies _species = PetSpecies.Cat;
    [ObservableProperty] private string _breed = string.Empty;

    public ObservableCollection<SpeciesTileViewModel> Tiles { get; } = [];
    public ObservableCollection<string> BreedOptions { get; } = [];

    /// <summary>Row-wise clinical alerts entered at registration (allergies). Task 3.</summary>
    public ObservableCollection<AlertRowEditViewModel> Allergies { get; } = [];

    /// <summary>Row-wise clinical alerts entered at registration (chronic conditions). Task 3.</summary>
    public ObservableCollection<AlertRowEditViewModel> ChronicConditions { get; } = [];

    public IReadOnlyList<string> Habitats => PatientsPresentation.Habitats;
    public IReadOnlyList<string> Behaviors => PatientsPresentation.Behaviors;
    public IReadOnlyList<string> Diets => PatientsPresentation.Diets;
    public IReadOnlyList<string> BloodTypes => PatientsPresentation.BloodTypes;

    /// <param name="defaultFemale">
    /// Which gender pill starts selected, matching each reference form: the
    /// owner wizard's «اطلاعات حیوان» defaults to «ماده» (female), while the
    /// add-pet modal defaults to «نر» (male).
    /// </param>
    public PetFormViewModel(bool defaultFemale = true)
    {
        foreach (var species in PatientsPresentation.SpeciesOrder)
        {
            Tiles.Add(new SpeciesTileViewModel(species));
        }

        if (defaultFemale)
        {
            IsFemale = true;   // auto-clears IsMale via OnIsFemaleChanged
        }
        else
        {
            IsMale = true;
        }

        SelectSpecies(PetSpecies.Cat);
    }

    partial void OnIsMaleChanged(bool value)
    {
        if (value) { IsFemale = false; IsUnknown = false; }
    }

    partial void OnIsFemaleChanged(bool value)
    {
        if (value) { IsMale = false; IsUnknown = false; }
    }

    partial void OnIsUnknownChanged(bool value)
    {
        if (value) { IsMale = false; IsFemale = false; }
    }

    /// <summary>Radio-pill gender picker: "female" | "male" | "unknown".</summary>
    [RelayCommand]
    private void SetGender(string? gender)
    {
        IsFemale = gender == "female";
        IsMale = gender == "male";
        IsUnknown = gender == "unknown";
    }

    /// <summary>The reference's «عقیم شده است» switch row (toggles the bool).</summary>
    [RelayCommand]
    private void ToggleNeutered() => Neutered = !Neutered;

    [RelayCommand]
    private void SelectSpecies(PetSpecies species)
    {
        Species = species;
        foreach (var tile in Tiles)
        {
            tile.IsActive = tile.Species == species;
        }

        BreedOptions.Clear();
        foreach (var b in PatientsPresentation.Breeds(species))
        {
            BreedOptions.Add(b);
        }

        Breed = BreedOptions.Count > 0 ? BreedOptions[0] : string.Empty;
    }

    /// <summary>Builds the create payload from the entered fields.</summary>
    public NewPetRequest BuildRequest()
    {
        var gender = IsFemale ? PetGender.Female : IsUnknown ? PetGender.Unknown : PetGender.Male;
        return new NewPetRequest(
            Name.Trim(),
            Species,
            Breed,
            Color.Trim(),
            gender,
            TryParseJalali(Birth, out var birth) ? birth : null,
            Habitat,
            Behavior,
            Diet,
            string.IsNullOrWhiteSpace(MicrochipCode) ? null : MicrochipCode.Trim(),
            TryParseJalali(MicrochipDate, out var chip) ? chip : null,
            Neutered,
            AlertTexts(Allergies),
            AlertTexts(ChronicConditions),
            string.IsNullOrWhiteSpace(BloodType) ? null : BloodType.Trim(),
            string.IsNullOrWhiteSpace(Insurance) ? null : Insurance.Trim(),
            string.IsNullOrWhiteSpace(MicrochipVendor) ? null : MicrochipVendor.Trim(),
            string.IsNullOrWhiteSpace(MicrochipLocation) ? null : MicrochipLocation.Trim(),
            Neutered && TryParseJalali(NeuteredDate, out var nd) ? nd : null);
    }

    /// <summary>Adds an empty allergy row to the registration form.</summary>
    [RelayCommand]
    private void AddAllergy() => Allergies.Add(new AlertRowEditViewModel(string.Empty, RemoveAllergy));

    private void RemoveAllergy(AlertRowEditViewModel row) => Allergies.Remove(row);

    /// <summary>Adds an empty chronic-condition row to the registration form.</summary>
    [RelayCommand]
    private void AddChronic() => ChronicConditions.Add(new AlertRowEditViewModel(string.Empty, RemoveChronic));

    private void RemoveChronic(AlertRowEditViewModel row) => ChronicConditions.Remove(row);

    /// <summary>Non-empty, trimmed alert lines from a row collection.</summary>
    internal static IReadOnlyList<string> AlertTexts(IEnumerable<AlertRowEditViewModel> rows) =>
        rows.Select(r => r.Text.Trim()).Where(t => t.Length > 0).ToArray();

    /// <summary>Whether the required minimum (a name) is present.</summary>
    public bool IsValid => !string.IsNullOrWhiteSpace(Name);

    internal static bool TryParseJalali(string input, out System.DateTime date)
    {
        date = default;
        var normalized = PersianNumberFormatter.ToAsciiDigits(input).Trim();
        var parts = normalized.Split('/', '-', '.');
        if (parts.Length != 3)
        {
            return false;
        }

        if (!int.TryParse(parts[0], out var y) ||
            !int.TryParse(parts[1], out var m) ||
            !int.TryParse(parts[2], out var d))
        {
            return false;
        }

        try
        {
            date = new JalaliDate(y, m, d).ToDateTime();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
