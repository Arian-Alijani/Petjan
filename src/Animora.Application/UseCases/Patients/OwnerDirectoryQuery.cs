using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.UseCases.Patients.ReadModels;
using Animora.Domain.Patients;
using Animora.Domain.Results;

namespace Animora.Application.UseCases.Patients;

/// <summary>Paged owner directory with optional search, species cross-filter and a stable
/// server-side sort (C01). When a species filter is set the owner set is narrowed to those
/// with a matching pet.</summary>
public sealed record OwnerDirectoryQuery(
    string? Search, PetSpecies? Species, PatientDirectorySort Sort, int Page, int PageSize)
    : IQuery<PagedResult<OwnerSummaryReadModel>>;

public sealed class OwnerDirectoryQueryHandler(IPatientDirectoryQueries queries)
    : IQueryHandler<OwnerDirectoryQuery, PagedResult<OwnerSummaryReadModel>>
{
    public async Task<Result<PagedResult<OwnerSummaryReadModel>>> HandleAsync(
        OwnerDirectoryQuery query, QueryContext context, CancellationToken ct) =>
        Result<PagedResult<OwnerSummaryReadModel>>.Ok(
            await queries.GetOwnersAsync(
                query.Search, query.Species, query.Sort, query.Page, query.PageSize, ct));
}

/// <summary>Directory facet counts (total owners/pets + per-species pet counts) for the toolbar's
/// result caption and species chip rail — resolved server-side alongside the first page (C01).</summary>
public sealed record PatientsDirectoryFacetsQuery(string? Search, PetSpecies? Species)
    : IQuery<PatientDirectoryFacetsReadModel>;

public sealed class PatientsDirectoryFacetsQueryHandler(IPatientDirectoryQueries queries)
    : IQueryHandler<PatientsDirectoryFacetsQuery, PatientDirectoryFacetsReadModel>
{
    public async Task<Result<PatientDirectoryFacetsReadModel>> HandleAsync(
        PatientsDirectoryFacetsQuery query, QueryContext context, CancellationToken ct) =>
        Result<PatientDirectoryFacetsReadModel>.Ok(
            await queries.GetFacetsAsync(query.Search, query.Species, ct));
}
