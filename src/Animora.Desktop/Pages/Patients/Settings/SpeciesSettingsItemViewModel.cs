using Animora.Contracts.Patients;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Patients.Settings;

/// <summary>
/// One species group in the Patients settings tab: a species (name + glyph) and
/// its editable breed list. Supports adding, inline-editing and deleting breeds,
/// renaming the species label and removing the whole species group.
/// <para>
/// UI-only for now (the collection lives in the ViewModel); the structure mirrors
/// the real taxonomy so a persistence service can be dropped in later without a
/// view change.
/// </para>
/// </summary>
public sealed partial class SpeciesSettingsItemViewModel : ObservableObject
{
    /// <summary>Raised when this species group asks to be removed from the list.</summary>
    public event Action<SpeciesSettingsItemViewModel>? RemoveRequested;

    public SpeciesSettingsItemViewModel(
        PetSpecies? species, string name, string iconKey, IEnumerable<string> breeds)
    {
        Species = species;
        _name = name;
        _draftName = name;
        IconKey = iconKey;
        foreach (var breed in breeds)
        {
            Breeds.Add(new BreedItemViewModel(breed));
        }
    }

    /// <summary>The underlying taxonomy value, or null for a user-added species.</summary>
    public PetSpecies? Species { get; }

    /// <summary>Icon resource key used for the species glyph.</summary>
    public string IconKey { get; }

    /// <summary>The committed species display name.</summary>
    [ObservableProperty]
    private string _name;

    /// <summary>In-progress value bound to the species-name inline editor.</summary>
    [ObservableProperty]
    private string _draftName;

    /// <summary>True while the species header is in inline-rename mode.</summary>
    [ObservableProperty]
    private bool _isEditingName;

    /// <summary>The draft text for the "add breed" input at the bottom of the group.</summary>
    [ObservableProperty]
    private string _newBreedName = string.Empty;

    public RangeObservableCollection<BreedItemViewModel> Breeds { get; } = [];

    public string BreedCountText =>
        PersianNumberFormatter.ToPersianDigits(Breeds.Count) + " نژاد";

    // ── species rename ────────────────────────────────────────────────────
    [RelayCommand]
    private void BeginRename()
    {
        DraftName = Name;
        IsEditingName = true;
    }

    [RelayCommand]
    private void CommitRename()
    {
        var value = (DraftName ?? string.Empty).Trim();
        if (value.Length > 0)
        {
            Name = value;
        }

        IsEditingName = false;
    }

    [RelayCommand]
    private void CancelRename() => IsEditingName = false;

    [RelayCommand]
    private void Remove() => RemoveRequested?.Invoke(this);

    // ── breed add / edit / delete ─────────────────────────────────────────
    [RelayCommand]
    private void AddBreed()
    {
        var value = (NewBreedName ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            return;
        }

        if (Breeds.Any(b => string.Equals(b.Name, value, StringComparison.OrdinalIgnoreCase)))
        {
            NewBreedName = string.Empty;
            return;
        }

        Breeds.Add(new BreedItemViewModel(value));
        NewBreedName = string.Empty;
        OnPropertyChanged(nameof(BreedCountText));
    }

    [RelayCommand]
    private static void BeginEditBreed(BreedItemViewModel? breed)
    {
        if (breed is null)
        {
            return;
        }

        breed.DraftName = breed.Name;
        breed.IsEditing = true;
    }

    [RelayCommand]
    private static void CommitEditBreed(BreedItemViewModel? breed)
    {
        if (breed is null)
        {
            return;
        }

        var value = (breed.DraftName ?? string.Empty).Trim();
        if (value.Length > 0)
        {
            breed.Name = value;
        }

        breed.IsEditing = false;
    }

    [RelayCommand]
    private static void CancelEditBreed(BreedItemViewModel? breed)
    {
        if (breed is not null)
        {
            breed.IsEditing = false;
        }
    }

    [RelayCommand]
    private void DeleteBreed(BreedItemViewModel? breed)
    {
        if (breed is not null && Breeds.Remove(breed))
        {
            OnPropertyChanged(nameof(BreedCountText));
        }
    }
}
