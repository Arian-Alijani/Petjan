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
    [ObservableProperty] private bool _neutered;

    [ObservableProperty] private bool _isMale = true;
    [ObservableProperty] private bool _isFemale;
    [ObservableProperty] private bool _isUnknown;
    [ObservableProperty] private PetSpecies _species = PetSpecies.Cat;
    [ObservableProperty] private string _breed = string.Empty;

    public ObservableCollection<SpeciesTileViewModel> Tiles { get; } = [];
    public ObservableCollection<string> BreedOptions { get; } = [];

    public IReadOnlyList<string> Habitats => PatientsPresentation.Habitats;
    public IReadOnlyList<string> Behaviors => PatientsPresentation.Behaviors;
    public IReadOnlyList<string> Diets => PatientsPresentation.Diets;

    public PetFormViewModel()
    {
        foreach (var species in PatientsPresentation.SpeciesOrder)
        {
            Tiles.Add(new SpeciesTileViewModel(species));
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
            Neutered);
    }

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
