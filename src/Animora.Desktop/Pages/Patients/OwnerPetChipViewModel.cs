using Animora.Contracts.Patients;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Avalonia.Media;

namespace Animora.Desktop.Pages.Patients;

/// <summary>
/// One pet row inside an owner card's pets sub-list, mirroring the reference's
/// <c>.pet-chip</c> (glyph, name + sex dot, "species · breed · age" sub-line and
/// a status pill).
/// </summary>
public sealed class OwnerPetChipViewModel
{
    public OwnerPetChipViewModel(PetSummary pet, OwnerSummary owner)
    {
        Pet = pet;
        Name = pet.Name;
        Sex = PatientsPresentation.SexDot(pet.Gender);
        SpeciesIconKey = PatientsPresentation.SpeciesIconKey(pet.Species);
        ToneBrush = PatientsPresentation.ToneBrush(pet.Tone);

        var age = RelativeDateFormatter.Age(pet.BirthDate);
        SubText =
            $"{PatientsPresentation.SpeciesLabel(pet.Species)} · {pet.Breed} · {age.Label}";

        StatusText = PatientsPresentation.StatusText(pet.Status);
        StatusTone = PatientsPresentation.StatusTone(pet.Status);
        OwnerInitials = PatientsPresentation.Initials(owner.FirstName, owner.LastName);
    }

    public PetSummary Pet { get; }
    public string Name { get; }
    public PatientsPresentation.SexDotInfo Sex { get; }
    public string SpeciesIconKey { get; }
    public IBrush ToneBrush { get; }
    public string SubText { get; }
    public string StatusText { get; }
    public Tone StatusTone { get; }
    public string OwnerInitials { get; }
}
