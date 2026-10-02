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

/// <summary>Owner sheet: profile + live pet roster.</summary>
public sealed record OwnerDetailReadModel(OwnerSummaryReadModel Owner, IReadOnlyList<PetSummaryReadModel> Pets);

/// <summary>Pet file: profile + weight history (visits/vaccinations arrive in C03).</summary>
public sealed record PetDetailReadModel(
    string Id, string Name, PetSpecies Species, string? Breed, string? Color, PetGender Gender,
    PetStatus Status, string OwnerId, DateTimeOffset? BirthDateUtc, bool IsNeutered, string? MicrochipCode,
    DateTimeOffset? MicrochipImplantUtc, string? AllergyNotes, string? ChronicNotes,
    IReadOnlyList<PetWeightReadModel> Weights);

/// <summary>One appended weight reading for the pet-file chart/rows.</summary>
public sealed record PetWeightReadModel(DateTimeOffset MeasuredUtc, decimal WeightKg);
