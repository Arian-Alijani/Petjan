using Animora.Domain.Patients;

namespace Animora.Application.UseCases.Patients.ReadModels;

/// <summary>Directory row for a pet — everything PetSummary needs, joined in one SQL statement (no N+1).</summary>
public sealed record PetSummaryReadModel(
    string Id, string Name, PetSpecies Species, string? Breed, string? Color, PetGender Gender,
    PetStatus Status, string OwnerId, DateTimeOffset? BirthDateUtc, decimal? CurrentWeightKg,
    DateTimeOffset? LastVisitUtc, string? LastVisitType, string? MicrochipCode);

/// <summary>Directory row for an owner with the joined pet count.</summary>
public sealed record OwnerSummaryReadModel(
    string Id, string FullName, string PhonePrimary, string? PhoneSecondary, string? NationalId,
    string? Address, OwnerGender Gender, DateTimeOffset RegisteredUtc, int PetCount);

/// <summary>
/// Stable server-side sort order for the directory projections. Pushed into the SQL
/// <c>ORDER BY</c> (always with a unique id tiebreaker) so paging never drifts.
/// </summary>
public enum PatientDirectorySort
{
    /// <summary>Owners: newest registration first; pets: name A–Z (no visit source yet in C01).</summary>
    Recent,

    /// <summary>Display name A–Z.</summary>
    Name,

    /// <summary>Owners: oldest registration first; pets: oldest birth date first (age).</summary>
    Age,
}

/// <summary>
/// Directory facet counts resolved for a given search/vaccine filter: total owners, total pets,
/// and per-species pet counts for the chip rail. The species map omits species with a zero count.
/// </summary>
public sealed record PatientDirectoryFacetsReadModel(
    int TotalOwners, int TotalPets, IReadOnlyDictionary<PetSpecies, int> SpeciesCounts);

/// <summary>
/// One clinical event (visit or recorded vaccine dose) of one of an owner's pets. <paramref name="Code"/> is the
/// visit-reason name for a visit and the vaccine name for a dose; the client maps it to display text.
/// </summary>
public sealed record OwnerClinicalEventRow(
    string Id, bool IsVisit, string PetId, string PetName, string Code, string? Detail, DateTimeOffset OccurredUtc, DateTimeOffset? NextDueUtc = null);

/// <summary>Newest clinical events of an owner (bounded) plus the real totals over the whole history.</summary>
public sealed record OwnerClinicalActivityReadModel(
    IReadOnlyList<OwnerClinicalEventRow> Events, int VisitCount, int VaccineCount);

/// <summary>Owner sheet: profile + live pet roster.</summary>
public sealed record OwnerDetailReadModel(OwnerSummaryReadModel Owner, IReadOnlyList<PetSummaryReadModel> Pets);

/// <summary>Pet file: profile + weight history + bounded newest-first visit history (vaccinations arrive in C03).</summary>
public sealed record PetDetailReadModel(
    string Id, string Name, PetSpecies Species, string? Breed, string? Color, PetGender Gender,
    PetStatus Status, string OwnerId, DateTimeOffset? BirthDateUtc, bool IsNeutered, string? MicrochipCode,
    DateTimeOffset? MicrochipImplantUtc, string? AllergyNotes, string? ChronicNotes,
    IReadOnlyList<PetWeightReadModel> Weights,
    IReadOnlyList<PetVisitReadModel>? Visits = null,
    IReadOnlyList<LabSummaryReadModel>? Labs = null,
    IReadOnlyList<RadiologySummaryReadModel>? Radiology = null,
    IReadOnlyList<PetVaccinationReadModel>? Vaccinations = null,
    string? Habitat = null, string? Behavior = null, string? Diet = null, string? BloodType = null,
    string? Insurance = null, string? MicrochipVendor = null, string? MicrochipLocation = null,
    DateTimeOffset? NeuteredUtc = null,
    IReadOnlyList<PetDocumentReadModel>? Documents = null);

/// <summary>
/// One laboratory result sheet flattened from a visit's JSON <c>LabResults</c> column for the
/// pet-level paraclinic read path. <paramref name="DateJalali"/> is already clinic-local and
/// formatted <c>yyyy/MM/dd</c>; <paramref name="Items"/> are the display-ready test values.
/// </summary>
public sealed record LabSummaryReadModel(
    string Id, string VisitId, string DateJalali, string? Description, IReadOnlyList<string> Items);

/// <summary>
/// One radiology study flattened from a visit's JSON <c>Radiology</c> column for the pet-level
/// paraclinic read path. <paramref name="DateJalali"/> is already clinic-local and formatted
/// <c>yyyy/MM/dd</c>; <paramref name="AttachmentCount"/> is the stored file count.
/// </summary>
public sealed record RadiologySummaryReadModel(
    string Id, string VisitId, string DateJalali, string BodyPart, string? Result, int AttachmentCount);

/// <summary>
/// One non-cancelled visit of a pet for the pet-file Visits tab. <paramref name="ReasonCode"/> is the
/// <c>VisitReason</c> name (the client maps it to display text); <paramref name="Total"/> is the billable
/// subtotal of the visit's service lines in minor units (tax is applied by the invoice, not here).
/// </summary>
public sealed record PetVisitReadModel(
    string Id, string ReasonCode, DateTimeOffset OccurredUtc, string DoctorName, string? ChiefComplaint,
    string? DoctorNotes, string? Diagnosis, IReadOnlyList<string> Prescriptions, decimal? WeightKg,
    decimal? TemperatureC, long Total, string? InvoiceId, int? PulseBpm = null, int? RespRate = null);

/// <summary>One appended weight reading for the pet-file chart/rows.</summary>
public sealed record PetWeightReadModel(DateTimeOffset MeasuredUtc, decimal WeightKg);

/// <summary>
/// One stored file of a pet for the pet-file documents tab (Task 10b). <paramref name="Id"/> is the
/// <c>DocumentReference</c> id; <paramref name="ContentHashSha256"/> is the content-addressed store key used to
/// open the payload, so the row can be downloaded without a second metadata read.
/// </summary>
public sealed record PetDocumentReadModel(
    string Id, string Title, int Kind, long SizeBytes, DateTimeOffset UploadedUtc, string ContentHashSha256);

/// <summary>
/// One recorded vaccine dose of a pet for the pet-file vaccinations tab (Task 9). <paramref name="AdministeredUtc"/>
/// is the injection time, <paramref name="NextDueUtc"/> the scheduled next dose (null when the course is complete
/// or undated), <paramref name="VetName"/> the administering clinician's display name (empty when unknown).
/// </summary>
public sealed record PetVaccinationReadModel(
    string Id, string VaccineName, DateTimeOffset AdministeredUtc, DateTimeOffset? NextDueUtc,
    int DoseNumber, int TotalDoses, string? LotNumber, string? Site, string VetName);
