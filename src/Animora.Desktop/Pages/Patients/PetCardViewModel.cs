using Animora.Contracts.Patients;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Avalonia.Media;

namespace Animora.Desktop.Pages.Patients;

/// <summary>
/// Adapts a <see cref="PetSummary"/> (plus its owner) for one pet card / pet
/// table row, pre-resolving every value the reference's <c>petCard</c> /
/// <c>petsTable</c> render — species glyph and label, cover gradient, sex dot,
/// status pill, and the formatted age / weight / last-visit / next-vaccine
/// captions — so the View binds plain strings and brushes.
/// </summary>
public sealed class PetCardViewModel
{
    public PetCardViewModel(PetSummary pet, OwnerSummary? owner)
    {
        Pet = pet;
        Name = pet.Name;
        Code = pet.Code;
        CodeText = $"کد بیمار {pet.Code}";
        Sex = PatientsPresentation.SexDot(pet.Gender);

        SpeciesLabel = PatientsPresentation.SpeciesLabel(pet.Species);
        SpeciesIconKey = PatientsPresentation.SpeciesIconKey(pet.Species);
        Breed = pet.Breed;
        BreedText = $"{SpeciesLabel} · {pet.Breed}";
        CoverBrush = PatientsPresentation.CoverBrush(pet.Species);
        ToneBrush = PatientsPresentation.ToneBrush(pet.Tone);

        StatusText = PatientsPresentation.StatusText(pet.Status);
        StatusTone = PatientsPresentation.StatusTone(pet.Status);

        var age = RelativeDateFormatter.Age(pet.BirthDate);
        AgeLongText = age.Label;
        AgeShortText = age.Short;

        WeightText = PatientsPresentation.WeightText(pet.CurrentWeightKg);

        LastVisitText = pet.LastVisitDate is { } last
            ? RelativeDateFormatter.Since(last)
            : "—";
        LastVisitType = pet.LastVisitType ?? string.Empty;

        // Pets table: next vaccine label + "N روز گذشته" when overdue.
        if (pet.NextVaccineDate is { } next)
        {
            var (days, label) = RelativeDateFormatter.Until(next);
            NextVaccineText = label;
            NextVaccineName = pet.NextVaccineName ?? string.Empty;
            IsNextVaccineOverdue = days < 0;
        }
        else
        {
            NextVaccineText = "—";
            NextVaccineName = string.Empty;
            IsNextVaccineOverdue = false;
        }

        // Owners table "واکسن" column uses the pet's vaccine presence.
        HasVaccines = pet.HasVaccines;

        OwnerName = owner is null ? "—" : $"{owner.FirstName} {owner.LastName}";
        OwnerMobile = owner?.Mobile1 ?? "—";
        OwnerInitials = owner is null
            ? "؟"
            : PatientsPresentation.Initials(owner.FirstName, owner.LastName);
        OwnerToneBrush = owner is null ? ToneBrush : PatientsPresentation.ToneBrush(owner.Tone);
    }

    public PetSummary Pet { get; }

    public string Name { get; }
    public string Code { get; }
    public string CodeText { get; }
    public PatientsPresentation.SexDotInfo Sex { get; }

    public string SpeciesLabel { get; }
    public string SpeciesIconKey { get; }
    public string Breed { get; }
    public string BreedText { get; }
    public IBrush CoverBrush { get; }
    public IBrush ToneBrush { get; }

    public string StatusText { get; }
    public Tone StatusTone { get; }

    public string AgeLongText { get; }
    public string AgeShortText { get; }
    public string WeightText { get; }
    public string LastVisitText { get; }
    public string LastVisitType { get; }

    /// <summary>Weight cell for the pets table ("۴.۴ کیلو" or the "—" placeholder).</summary>
    public string WeightTableText => Pet.CurrentWeightKg is null ? "—" : $"{WeightText} کیلو";

    public string NextVaccineText { get; }
    public string NextVaccineName { get; }
    public bool IsNextVaccineOverdue { get; }

    public bool HasVaccines { get; }

    public string OwnerName { get; }
    public string OwnerMobile { get; }
    public string OwnerInitials { get; }
    public IBrush OwnerToneBrush { get; }

    public string VaccineStateText => HasVaccines ? "دارد" : "ندارد";
    public Tone VaccineStateTone => HasVaccines ? Tone.Ok : Tone.Neutral;
}
