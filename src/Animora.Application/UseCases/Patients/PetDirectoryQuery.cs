using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.UseCases.Patients.ReadModels;
using Animora.Domain.Patients;
using Animora.Domain.Results;

namespace Animora.Application.UseCases.Patients;

/// <summary>Paged pet directory with optional search + species/status filters (C01).</summary>
public sealed record PetDirectoryQuery(string? Search, PetSpecies? Species, PetStatus? Status, int Page, int PageSize)
    : IQuery<PagedResult<PetSummaryReadModel>>;

public sealed class PetDirectoryQueryHandler(IPatientDirectoryQueries queries)
    : IQueryHandler<PetDirectoryQuery, PagedResult<PetSummaryReadModel>>
{
    public async Task<Result<PagedResult<PetSummaryReadModel>>> HandleAsync(
        PetDirectoryQuery query, QueryContext context, CancellationToken ct) =>
        Result<PagedResult<PetSummaryReadModel>>.Ok(
            await queries.GetPetsAsync(query.Search, query.Species, query.Status, query.Page, query.PageSize, ct));
}
