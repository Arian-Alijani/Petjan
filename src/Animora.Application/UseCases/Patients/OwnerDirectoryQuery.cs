using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.UseCases.Patients.ReadModels;
using Animora.Domain.Results;

namespace Animora.Application.UseCases.Patients;

/// <summary>Paged owner directory with optional search (C01).</summary>
public sealed record OwnerDirectoryQuery(string? Search, int Page, int PageSize)
    : IQuery<PagedResult<OwnerSummaryReadModel>>;

public sealed class OwnerDirectoryQueryHandler(IPatientDirectoryQueries queries)
    : IQueryHandler<OwnerDirectoryQuery, PagedResult<OwnerSummaryReadModel>>
{
    public async Task<Result<PagedResult<OwnerSummaryReadModel>>> HandleAsync(
        OwnerDirectoryQuery query, QueryContext context, CancellationToken ct) =>
        Result<PagedResult<OwnerSummaryReadModel>>.Ok(
            await queries.GetOwnersAsync(query.Search, query.Page, query.PageSize, ct));
}
