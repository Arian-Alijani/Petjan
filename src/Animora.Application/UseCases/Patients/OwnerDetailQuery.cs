using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.UseCases.Patients.ReadModels;
using Animora.Domain.Results;

namespace Animora.Application.UseCases.Patients;

/// <summary>Owner sheet read: profile + live pet roster, or null when unknown (C01).</summary>
public sealed record OwnerDetailQuery(string OwnerId) : IQuery<OwnerDetailReadModel?>;

public sealed class OwnerDetailQueryHandler(IPatientDirectoryQueries queries)
    : IQueryHandler<OwnerDetailQuery, OwnerDetailReadModel?>
{
    public async Task<Result<OwnerDetailReadModel?>> HandleAsync(
        OwnerDetailQuery query, QueryContext context, CancellationToken ct) =>
        Result<OwnerDetailReadModel?>.Ok(await queries.GetOwnerAsync(query.OwnerId, ct));
}
