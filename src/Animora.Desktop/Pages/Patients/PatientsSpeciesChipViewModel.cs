using Animora.Contracts.Patients;
using Animora.Desktop.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Animora.Desktop.Pages.Patients;

/// <summary>
/// One species filter chip in the toolbar's chip rail (mirrors the reference's
/// <c>renderSpeciesChips</c>): a species value (null = "همه گونه‌ها"), its glyph
/// and a derived count. <see cref="IsActive"/> drives the selected styling.
/// </summary>
public sealed partial class PatientsSpeciesChipViewModel : ObservableObject
{
    public PatientsSpeciesChipViewModel(PetSpecies? value, string label, string iconKey, int count)
    {
        Value = value;
        Label = label;
        IconKey = iconKey;
        CountText = PersianNumberFormatter.ToPersianDigits(count);
    }

    public PetSpecies? Value { get; }

    /// <summary>The species as a command parameter string ("all" / enum name).</summary>
    public string CommandValue => Value?.ToString() ?? "all";

    public string Label { get; }

    public string IconKey { get; }

    public string CountText { get; }

    [ObservableProperty]
    private bool _isActive;
}
