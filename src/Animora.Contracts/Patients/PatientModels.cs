namespace Animora.Contracts.Patients;

/// <summary>
/// Animal species offered by the directory, mirroring the reference's species
/// taxonomy (Animora-UI/assets/patients-data.js SPECIES) — the set that drives
/// the species filter chips and each card's animal glyph.
/// </summary>
public enum PetSpecies
{
    Cat,
    Dog,
    Bird,
    Rabbit,
    Rodent,
    Other,
}

/// <summary>
/// Clinical status shown as the card/table status pill, mirroring the
/// reference's STATUS_MAP (healthy / followup / critical → ok / warn / danger).
/// </summary>
public enum PetStatus
{
    Healthy,
    FollowUp,
    Critical,
}

/// <summary>Animal sex, mirrored by the reference's GENDER_MAP (♂/♀/؟).</summary>
public enum PetGender
{
    Male,
    Female,
    Unknown,
}

/// <summary>Owner sex, mirrored by the reference's OWNER_GENDER (آقا/خانم).</summary>
public enum OwnerGender
{
    Female,
    Male,
}

/// <summary>
/// Avatar/cover colour tone (A–F), mirroring the reference's `.tone-*`
/// gradient set so a card's avatar keeps the same colour it has in the
/// prototype.
/// </summary>
public enum PatientAvatarTone
{
    A,
    B,
    C,
    D,
    E,
    F,
}

/// <summary>
/// One animal (patient) row for the directory, shaped to exactly what the
/// reference's <c>petCard</c>/<c>petsTable</c>/<c>pet-chip</c> render: identity,
/// species/breed, owner link, an age/weight snapshot and the last-visit /
/// next-vaccine dates. Dates are stored Gregorian and formatted through the
/// Phase 04 Jalali helpers by the view layer.
/// </summary>
public sealed record PetSummary(
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
    DateTime BirthDate,
    double? CurrentWeightKg,
    DateTime? LastVisitDate,
    string? LastVisitType,
    bool HasVaccines,
    string? NextVaccineName,
    DateTime? NextVaccineDate,
    string? MicrochipCode);

/// <summary>
/// One owner row for the directory, shaped to exactly what the reference's
/// <c>ownerCard</c>/<c>ownersTable</c> render: identity, contact block, file
/// number, tags, referral source, join date and account balance. The owner's
/// animals are derived by the view layer from the pet list (single source of
/// truth — no de-normalized pet data on the owner).
/// </summary>
public sealed record OwnerSummary(
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
    long Balance,
    IReadOnlyList<string> Tags);
