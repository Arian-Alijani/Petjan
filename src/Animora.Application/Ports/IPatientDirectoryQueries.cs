using Animora.Application.Abstractions;
using Animora.Application.UseCases.Patients.ReadModels;
using Animora.Domain.Patients;

namespace Animora.Application.Ports;

/// <summary>Projection reads backing the Patients directory + detail screens (C01). One SQL per method, no N+1.</summary>
public interface IPatientDirectoryQueries
{
    Task<PagedResult<PetSummaryReadModel>> GetPetsAsync(
        string? search, PetSpecies? species, PetStatus? status, int page, int pageSize, CancellationToken ct);

    Task<PagedResult<OwnerSummaryReadModel>> GetOwnersAsync(
        string? search, int page, int pageSize, CancellationToken ct);

    Task<OwnerDetailReadModel?> GetOwnerAsync(string ownerId, CancellationToken ct);

    Task<PetDetailReadModel?> GetPetAsync(string petId, CancellationToken ct);
}
