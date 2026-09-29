using Animora.Application.UseCases.Patients.ReadModels;
using Animora.Contracts.Patients;
using DomainSpecies = Animora.Domain.Patients.PetSpecies;
using DomainPetGender = Animora.Domain.Patients.PetGender;
using DomainPetStatus = Animora.Domain.Patients.PetStatus;
using DomainOwnerGender = Animora.Domain.Patients.OwnerGender;

namespace Animora.Client.Services.Patients;

/// <summary>Hand-written read-model ↔ Contracts DTO mapping (C01 pattern — no AutoMapper).</summary>
internal static class PatientsMapping
{
    public static PetSummary ToSummary(this PetSummaryReadModel m) => new(
        m.Id, m.Id /*Code*/, m.Name, MapSpecies(m.Species), m.Breed ?? "", m.Color ?? "",
        MapGender(m.Gender), Tone(m.Id), MapStatus(m.Status), m.OwnerId,
        (m.BirthDateUtc ?? default).UtcDateTime, (double?)m.CurrentWeightKg,
        m.LastVisitUtc?.UtcDateTime, m.LastVisitType, HasVaccines: false, NextVaccineName: null,
        NextVaccineDate: null, m.MicrochipCode);

    public static OwnerSummary ToSummary(this OwnerSummaryReadModel m)
    {
        var (first, last) = SplitName(m.FullName);
        return new OwnerSummary(m.Id, m.Id /*File*/, first, last, MapOwnerGender(m.Gender),
            m.PhonePrimary, m.PhoneSecondary, Phone: null, m.NationalId ?? "", m.Address, Referral: null,
            m.RegisteredUtc.UtcDateTime, Tone(m.Id), m.TotalSpend, Tags: []);
    }

    public static OwnerDetail ToDetail(this OwnerDetailReadModel m)
    {
        var s = m.Owner.ToSummary();
        return new OwnerDetail(s.Id, s.File, s.FirstName, s.LastName, s.Gender, s.Mobile1, s.Mobile2,
            s.Phone, s.NationalId, s.Address, s.Referral, s.Since, s.Tone, s.Balance, s.Tags,
            m.Pets.Select(p => p.ToSummary()).ToList());
    }

    public static PetDetail ToDetail(this PetDetailReadModel m) => new(
        m.Id, m.Id, m.Name, MapSpecies(m.Species), m.Breed ?? "", m.Color ?? "", MapGender(m.Gender),
        Tone(m.Id), MapStatus(m.Status), m.OwnerId, (m.BirthDateUtc ?? default).UtcDateTime,
        Habitat: "", Behavior: "", Diet: "", BloodType: null, Neutered: m.IsNeutered, NeuteredDate: null,
        Insurance: null,
        new MicrochipInfo(m.MicrochipCode, m.MicrochipImplantUtc?.UtcDateTime, null, null),
        new PetVitals(0, 0, 0, ""),
        m.Weights.Select(w => new WeightEntry(w.MeasuredUtc.UtcDateTime, (double)w.WeightKg)).ToList(),
        Visits: [], Vaccinations: [], Labs: [], Documents: [], Surgeries: [],
        Allergies: Split(m.AllergyNotes), ChronicConditions: Split(m.ChronicNotes), Notes: null);

    private static (string First, string Last) SplitName(string full)
    {
        var i = full.IndexOf(' ');
        return i < 0 ? (full, "") : (full[..i], full[(i + 1)..]);
    }

    /// <summary>Splits a comma/newline separated clinical note into a trimmed, non-empty list.
    /// Accepts both the Latin comma ',' and the Persian comma '،' (U+060C) as separators.</summary>
    private static IReadOnlyList<string> Split(string? notes) => string.IsNullOrWhiteSpace(notes)
        ? []
        : notes.Split([',', '،', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Stable, id-derived avatar tone (sum of char codes mod 6) so the UI colour is reproducible.</summary>
    private static PatientAvatarTone Tone(string id)
    {
        var sum = 0;
        foreach (var c in id) sum = (sum + c) % 6;
        return (PatientAvatarTone)sum;
    }

    private static PetSpecies MapSpecies(DomainSpecies s) => s switch
    {
        DomainSpecies.Dog => PetSpecies.Dog,
        DomainSpecies.Cat => PetSpecies.Cat,
        DomainSpecies.Bird => PetSpecies.Bird,
        DomainSpecies.Rodent => PetSpecies.Rodent,
        _ => PetSpecies.Other,
    };

    private static PetGender MapGender(DomainPetGender g) => g switch
    {
        DomainPetGender.Male => PetGender.Male,
        DomainPetGender.Female => PetGender.Female,
        _ => PetGender.Unknown,
    };

    private static PetStatus MapStatus(DomainPetStatus s) => s switch
    {
        DomainPetStatus.Active => PetStatus.Healthy,
        DomainPetStatus.Inactive => PetStatus.FollowUp,
        _ => PetStatus.Critical,
    };

    private static OwnerGender MapOwnerGender(DomainOwnerGender g) => g switch
    {
        DomainOwnerGender.Female => OwnerGender.Female,
        _ => OwnerGender.Male,
    };

    // Contracts → command inputs (for CreateOwner/CreatePet/etc.):
    public static DomainSpecies ToDomain(this PetSpecies s) => s switch
    {
        PetSpecies.Dog => DomainSpecies.Dog,
        PetSpecies.Cat => DomainSpecies.Cat,
        PetSpecies.Bird => DomainSpecies.Bird,
        PetSpecies.Rodent => DomainSpecies.Rodent,
        PetSpecies.Rabbit => DomainSpecies.Other,
        _ => DomainSpecies.Other,
    };

    public static DomainPetGender ToDomain(this PetGender g) => g switch
    {
        PetGender.Male => DomainPetGender.Male,
        PetGender.Female => DomainPetGender.Female,
        _ => DomainPetGender.Unknown,
    };

    public static DomainOwnerGender ToDomain(this OwnerGender g) => g switch
    {
        OwnerGender.Female => DomainOwnerGender.Female,
        _ => DomainOwnerGender.Male,
    };
}
