using Animora.Domain.Patients;

namespace Animora.Application.UseCases.Patients.ReadModels;

/// <summary>Directory row for a pet — everything PetSummary needs, joined in one SQL statement (no N+1).</summary>
public sealed record PetSummaryReadModel(
    string Id, string Name, PetSpecies Species, string? Breed, string? Color, PetGender Gender,
    PetStatus Status, string OwnerId, DateTimeOffset? BirthDateUtc, decimal? CurrentWeightKg,
    DateTimeOffset? LastVisitUtc, string? LastVisitType, string? MicrochipCode);

/// <summary>Directory row for an owner. PetCount joined; TotalSpend deferred to C09 (always 0 here).</summary>
public sealed record OwnerSummaryReadModel(
    string Id, string FullName, string PhonePrimary, string? PhoneSecondary, string? NationalId,
    string? Address, OwnerGender Gender, DateTimeOffset RegisteredUtc, int PetCount, long TotalSpend);

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
