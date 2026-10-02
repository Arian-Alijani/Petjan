namespace Animora.Contracts.Patients;

/// <summary>
/// Result state of a lab/imaging study, mirroring the reference's
/// <c>labs[].status</c> map (normal → ok, warn → warn, danger → danger,
/// pending → info) used by the pet file's documents tab.
/// </summary>
public enum LabStatus
{
    Normal,
    Warn,
    Danger,
    Pending,
}

/// <summary>
/// Implanted microchip block for a pet, matching the reference's
/// <c>pet.microchip</c>. A pet without a chip carries an empty
/// <see cref="Code"/> and the info tab omits the whole chip panel.
/// </summary>
public sealed record MicrochipInfo(
    string? Code,
    DateTime? ImplantDate,
    string? Vendor,
    string? Location);

/// <summary>
/// Last-examination vitals, matching the reference's <c>pet.vitals</c>
/// (temp/pulse/resp/bcs) rendered by the pet file's summary tab.
/// </summary>
public sealed record PetVitals(
    double Temperature,
    int Pulse,
    int Respiration,
    string BodyConditionScore);

/// <summary>One weight measurement (Jalali-labelled in the UI), used by the weight chart + rows.</summary>
public sealed record WeightEntry(DateTime Date, double WeightKg);

/// <summary>A surgical-history entry, matching the reference's <c>pet.surgeries[]</c>.</summary>
public sealed record SurgeryRef(string Title, DateTime Date, string Surgeon, string? Note);

/// <summary>
/// Minimal read-only visit reference for the pet file's visits tab. It carries
/// the fields the reference's accordion renders (type/date/vet/reason + the exam,
/// diagnosis, prescriptions and per-visit vitals/cost) so the tab can be shown in
/// full once the Visits context supplies rows — no full Visit contract needed yet.
/// </summary>
public sealed record VisitSummaryRef(
    string Id,
    string Type,
    DateTime Date,
    string Vet,
    string Reason,
    string Exam,
    string Diagnosis,
    IReadOnlyList<string> Prescriptions,
    double WeightKg,
    double Temperature,
    long Cost);

/// <summary>
/// Minimal read-only vaccination reference for the pet file's vaccinations tab,
/// matching the reference's <c>pet.vaccines[]</c> (name/date/next/lot/vet/site).
/// </summary>
public sealed record VaccinationSummaryRef(
    string Name,
    DateTime Date,
    DateTime NextDate,
    string Lot,
    string Vet,
    string Site);

/// <summary>Minimal read-only lab/imaging reference for the documents tab.</summary>
public sealed record LabSummaryRef(string Name, DateTime Date, string Result, LabStatus Status);

/// <summary>A stored document reference for the documents tab.</summary>
public sealed record DocumentRef(string Name, string Size, DateTime Date);

/// <summary>
/// Full pet record backing the Pet File screen, plus its owner id. Combines the
/// directory-level <see cref="PetSummary"/> identity with the profile extras the
/// pet file renders (habitat/behavior/diet/blood/insurance/neutering), the
/// microchip block, the latest vitals, and the weight / visit / vaccination /
/// lab / document / surgery collections.
/// </summary>
public sealed record PetDetail(
    string Id,
    string Code,
    string Name,
    PetSpecies Species,
    string Breed,
    string Color,
    PetGender Gender,
    PatientAvatarTone Tone,
    PetStatus Status,
    string OwnerId,
    DateTime? BirthDate,
    string Habitat,
    string Behavior,
    string Diet,
    string? BloodType,
    bool Neutered,
    DateTime? NeuteredDate,
    string? Insurance,
    MicrochipInfo Microchip,
    PetVitals Vitals,
    IReadOnlyList<WeightEntry> Weights,
    IReadOnlyList<VisitSummaryRef> Visits,
    IReadOnlyList<VaccinationSummaryRef> Vaccinations,
    IReadOnlyList<LabSummaryRef> Labs,
    IReadOnlyList<DocumentRef> Documents,
    IReadOnlyList<SurgeryRef> Surgeries,
    IReadOnlyList<string> Allergies,
    IReadOnlyList<string> ChronicConditions,
    string? Notes);

/// <summary>
/// Full owner record backing the Owner Sheet: the directory-level
/// <see cref="OwnerSummary"/> identity plus the owner's live pet roster.
/// </summary>
public sealed record OwnerDetail(
    string Id,
    string File,
    string FirstName,
    string LastName,
    OwnerGender Gender,
    string Mobile1,
    string? Mobile2,
    string? Phone,
    string NationalId,
    string? Address,
    string? Referral,
    DateTime Since,
    PatientAvatarTone Tone,
    IReadOnlyList<string> Tags,
    IReadOnlyList<PetSummary> Pets);

/// <summary>
/// Registration payload for a new pet, produced by the add-pet/add-owner wizard.
/// Owner-identifying fields are absent because the owner is either already known
/// (id passed alongside) or created by <see cref="NewOwnerRequest"/> first.
/// </summary>
public sealed record NewPetRequest(
    string Name,
    PetSpecies Species,
    string Breed,
    string Color,
    PetGender Gender,
    DateTime? BirthDate,
    string Habitat,
    string Behavior,
    string Diet,
    string? MicrochipCode,
    DateTime? MicrochipDate,
    bool Neutered);

/// <summary>
/// Registration payload for a brand-new owner (and, when present, their first
/// pet) produced by the add-owner wizard.
/// </summary>
public sealed record NewOwnerRequest(
    string FirstName,
    string LastName,
    OwnerGender Gender,
    string NationalId,
    string Mobile1,
    string? Mobile2,
    string? Phone,
    string? Address,
    string? Referral,
    NewPetRequest? Pet);
