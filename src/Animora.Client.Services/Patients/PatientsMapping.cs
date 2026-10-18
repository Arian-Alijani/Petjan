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
        m.BirthDateUtc?.UtcDateTime, (double?)m.CurrentWeightKg,
        // LastVisitUtc/LastVisitType come from the latest visit (reason code mapped to its Persian label).
        // HasVaccines/NextVaccine* stay false/null until the vaccine source arrives.
        m.LastVisitUtc?.UtcDateTime, m.LastVisitType is { } reason ? VisitReasonLabel(reason) : null,
        HasVaccines: false, NextVaccineName: null,
        NextVaccineDate: null, m.MicrochipCode);

    public static OwnerSummary ToSummary(this OwnerSummaryReadModel m)
    {
        var (first, last) = SplitName(m.FullName);
        return new OwnerSummary(m.Id, m.Id /*File*/, first, last, MapOwnerGender(m.Gender),
            m.PhonePrimary, m.PhoneSecondary, Phone: null, m.NationalId ?? "", m.Address, Referral: null,
            m.RegisteredUtc.UtcDateTime, Tone(m.Id), Tags: [], PetCount: m.PetCount);
    }

    public static OwnerDetail ToDetail(this OwnerDetailReadModel m)
    {
        var s = m.Owner.ToSummary();
        return new OwnerDetail(s.Id, s.File, s.FirstName, s.LastName, s.Gender, s.Mobile1, s.Mobile2,
            s.Phone, s.NationalId, s.Address, s.Referral, s.Since, s.Tone, s.Tags,
            m.Pets.Select(p => p.ToSummary()).ToList());
    }

    public static PetDetail ToDetail(this PetDetailReadModel m)
    {
        // Last-exam vitals come from the newest non-cancelled visit (visits are already newest-first in the
        // read model). BCS has no domain source yet, so it stays "—" (empty) — the VM renders the dash.
        var newest = (m.Visits ?? []).FirstOrDefault();
        var vitals = newest is null
            ? new PetVitals(0, 0, 0, "")
            : new PetVitals((double)(newest.TemperatureC ?? 0m), newest.PulseBpm ?? 0, newest.RespRate ?? 0, "");

        return new(
            m.Id, m.Id, m.Name, MapSpecies(m.Species), m.Breed ?? "", m.Color ?? "", MapGender(m.Gender),
            Tone(m.Id), MapStatus(m.Status), m.OwnerId, m.BirthDateUtc?.UtcDateTime,
            m.Habitat ?? "", m.Behavior ?? "", m.Diet ?? "", m.BloodType, Neutered: m.IsNeutered,
            NeuteredDate: m.NeuteredUtc?.UtcDateTime, Insurance: m.Insurance,
            new MicrochipInfo(m.MicrochipCode, m.MicrochipImplantUtc?.UtcDateTime, m.MicrochipVendor, m.MicrochipLocation),
            vitals,
            m.Weights.Select(w => new WeightEntry(w.MeasuredUtc.UtcDateTime, (double)w.WeightKg)).ToList(),
            Visits: (m.Visits ?? []).Select(ToVisitRef).ToList(),
            // Task 9: the real dose history from VaccinationRecord (was hardcoded []).
            Vaccinations: (m.Vaccinations ?? []).Select(ToVaccinationRef).ToList(),
            Labs: [], Documents: [], Surgeries: [],
            Allergies: Split(m.AllergyNotes), ChronicConditions: Split(m.ChronicNotes), Notes: null);
    }

    /// <summary>
    /// One dose → the pet-file row. The stored <c>NextDueUtc</c> is null once a course ends, but the model
    /// still wants a reminder date: fall back to the administered date so "next due" never renders a fake 0001 date.
    /// </summary>
    private static VaccinationSummaryRef ToVaccinationRef(PetVaccinationReadModel v) => new(
        v.VaccineName,
        v.AdministeredUtc.UtcDateTime,
        (v.NextDueUtc ?? v.AdministeredUtc).UtcDateTime,
        string.IsNullOrWhiteSpace(v.LotNumber) ? "—" : v.LotNumber!,
        string.IsNullOrWhiteSpace(v.VetName) ? "—" : v.VetName,
        string.IsNullOrWhiteSpace(v.Site) ? "—" : v.Site!);

    private static VisitSummaryRef ToVisitRef(PetVisitReadModel v)
    {
        var complaint = v.ChiefComplaint ?? string.Empty;
        return new VisitSummaryRef(
            v.Id, VisitReasonLabel(v.ReasonCode), v.OccurredUtc.UtcDateTime, v.DoctorName,
            complaint, v.DoctorNotes ?? string.Empty, v.Diagnosis ?? string.Empty, v.Prescriptions,
            (double)(v.WeightKg ?? 0m), (double)(v.TemperatureC ?? 0m), v.Total, v.InvoiceId);
    }

    /// <summary>Persian display label of a <c>VisitReason</c> name (shared wording with the owner activity feed).</summary>
    internal static string VisitReasonLabel(string reason) => reason switch
    {
        "Checkup" => "معاینه عمومی",
        "Illness" => "بیماری",
        "Injury" => "سانحه",
        "Vaccination" => "واکسیناسیون",
        "Surgery" => "جراحی",
        "Dental" => "دندان‌پزشکی",
        "Followup" => "پیگیری",
        _ => "عمومی",
    };

    /// <summary>
    /// Best-effort DISPLAY decomposition of the owner's single identity. The domain stores one
    /// <c>FullName</c> (source of truth) — it has no separate first/last. First = first token,
    /// Last = the remainder. Saves re-join with a single space (<c>$"{First} {Last}".Trim()</c>), so
    /// the split is loss-free on round-trip: multi-part names ("محمد رضا تقوی") and single-token names
    /// (Last = "") are both preserved verbatim.
    /// </summary>
    private static (string First, string Last) SplitName(string full)
    {
        var i = full.IndexOf(' ');
        return i < 0 ? (full, "") : (full[..i], full[(i + 1)..]);
    }

    /// <summary>Splits a newline-separated clinical note into a trimmed, non-empty list. Commas (Latin or
    /// Persian) are preserved inside a value so an entry like «دارو، نوع ب» survives a save/reload round-trip.
    /// Back-compat: rows previously joined with ", " now read as one entry — the text is kept verbatim, only the
    /// list segmentation differs (pre-release, no production data).</summary>
    private static IReadOnlyList<string> Split(string? notes) => string.IsNullOrWhiteSpace(notes)
        ? []
        : notes.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Inverse of <see cref="Split"/>: joins list entries with a newline (one entry per line).</summary>
    public static string JoinList(IEnumerable<string> items) => string.Join('\n', items);

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
        DomainSpecies.Reptile => PetSpecies.Reptile,
        DomainSpecies.Rabbit => PetSpecies.Rabbit,
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
        DomainPetStatus.Inactive => PetStatus.Inactive,
        DomainPetStatus.Deceased => PetStatus.Deceased,
        _ => PetStatus.FollowUp,
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
        PetSpecies.Rabbit => DomainSpecies.Rabbit,
        PetSpecies.Reptile => DomainSpecies.Reptile,
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
