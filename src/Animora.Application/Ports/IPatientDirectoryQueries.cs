using Animora.Application.Abstractions;
using Animora.Application.UseCases.Patients.ReadModels;
using Animora.Domain.Patients;

namespace Animora.Application.Ports;

/// <summary>Projection reads backing the Patients directory + detail screens (C01). One SQL per method, no N+1.</summary>
public interface IPatientDirectoryQueries
{
    Task<PagedResult<PetSummaryReadModel>> GetPetsAsync(
        string? search, PetSpecies? species, PetStatus? status, PatientDirectorySort sort,
        int page, int pageSize, CancellationToken ct);

    Task<PagedResult<OwnerSummaryReadModel>> GetOwnersAsync(
        string? search, PetSpecies? species, PetStatus? status, PatientDirectorySort sort,
        int page, int pageSize, CancellationToken ct);

    /// <summary>Directory facet counts (total owners, total pets, per-species pet counts) for the
    /// given search/species/status filter. The species breakdown ignores <paramref name="species"/>
    /// so the chip rail always shows the full taxonomy under the current text/status filter.</summary>
    Task<PatientDirectoryFacetsReadModel> GetFacetsAsync(
        string? search, PetSpecies? species, PetStatus? status, CancellationToken ct);

    Task<OwnerDetailReadModel?> GetOwnerAsync(string ownerId, CancellationToken ct);

    Task<PetDetailReadModel?> GetPetAsync(string petId, CancellationToken ct);
}
