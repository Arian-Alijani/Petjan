using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.UseCases.Patients.ReadModels;
using Animora.Domain.Results;

namespace Animora.Application.UseCases.Patients;

/// <summary>Pet file read: profile + weight history, or null when unknown (C01).</summary>
public sealed record PetDetailQuery(string PetId) : IQuery<PetDetailReadModel?>;

public sealed class PetDetailQueryHandler(IPatientDirectoryQueries queries)
    : IQueryHandler<PetDetailQuery, PetDetailReadModel?>
{
    public async Task<Result<PetDetailReadModel?>> HandleAsync(
        PetDetailQuery query, QueryContext context, CancellationToken ct) =>
        Result<PetDetailReadModel?>.Ok(await queries.GetPetAsync(query.PetId, ct));
}
