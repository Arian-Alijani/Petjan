using CommunityToolkit.Mvvm.ComponentModel;

namespace Animora.Desktop.Pages.Patients.Settings;

/// <summary>
/// One editable breed entry under a species in the Patients settings tab
/// («تنظیم نوع حیوانات و نژادها»). Holds the breed's display name plus a
/// lightweight inline-edit state so the row can flip between read and edit mode
/// without a modal. Kept UI-only for now — the backing store is scaffolded in
/// <see cref="PatientsSettingsViewModel"/> ready to be wired to a real service.
/// </summary>
public sealed partial class BreedItemViewModel : ObservableObject
{
    public BreedItemViewModel(string name)
    {
        _name = name;
        _draftName = name;
    }

    /// <summary>The committed breed name shown on the chip.</summary>
    [ObservableProperty]
    private string _name;

    /// <summary>The in-progress value bound to the inline editor text box.</summary>
    [ObservableProperty]
    private string _draftName;

    /// <summary>True while the row is in inline-edit mode.</summary>
    [ObservableProperty]
    private bool _isEditing;
}
