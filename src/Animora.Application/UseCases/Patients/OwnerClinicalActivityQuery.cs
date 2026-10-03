using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.UseCases.Patients.ReadModels;
using Animora.Domain.Identity;
using Animora.Domain.Results;

namespace Animora.Application.UseCases.Patients;

/// <summary>Owner file activity feed (clinical half): visits + vaccine doses of the owner's pets, bounded.</summary>
[RequiresPermission(PermissionCodes.PatientsRead)]
public sealed record OwnerClinicalActivityQuery(string OwnerId, int Take = 50) : IQuery<OwnerClinicalActivityReadModel>;

public sealed class OwnerClinicalActivityQueryHandler(IPatientDirectoryQueries queries)
    : IQueryHandler<OwnerClinicalActivityQuery, OwnerClinicalActivityReadModel>
{
    private const int HardMaxTake = 200;

    public async Task<Result<OwnerClinicalActivityReadModel>> HandleAsync(
        OwnerClinicalActivityQuery query, QueryContext context, CancellationToken ct) =>
        Result<OwnerClinicalActivityReadModel>.Ok(await queries.GetOwnerClinicalActivityAsync(
            query.OwnerId, Math.Clamp(query.Take, 1, HardMaxTake), ct));
}
