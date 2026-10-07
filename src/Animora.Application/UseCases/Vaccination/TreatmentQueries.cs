using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Domain.Clinical;
using Animora.Domain.Identity;
using Animora.Domain.Results;

namespace Animora.Application.UseCases.Vaccination;

/// <summary>
/// The scheduled medication / vaccine days inside a window of at most <see cref="MaxRangeDays"/> days
/// (a month view plus its leading/trailing weeks), optionally narrowed by kind, item type and patient.
/// </summary>
[RequiresPermission(PermissionCodes.VaccinationRead)]
public sealed record TreatmentCalendarQuery(DateOnly From, DateOnly To, IReadOnlyCollection<TreatmentKind>? Kinds = null,
    TreatmentItemType? ItemType = null, string? PetId = null) : IQuery<IReadOnlyList<TreatmentCalendarRow>>
{
    /// <summary>Widest accepted window, inclusive.</summary>
    public const int MaxRangeDays = 62;

    /// <summary>Hard row cap; a window that exceeds it is truncated, never unbounded.</summary>
    public const int MaxRows = 2000;
}

public sealed class TreatmentCalendarQueryHandler(ITreatmentPlanQueries queries)
    : IQueryHandler<TreatmentCalendarQuery, IReadOnlyList<TreatmentCalendarRow>>
{
    public async Task<Result<IReadOnlyList<TreatmentCalendarRow>>> HandleAsync(TreatmentCalendarQuery q,
        QueryContext context, CancellationToken ct)
    {
        if (q.To < q.From || q.To.DayNumber - q.From.DayNumber + 1 > TreatmentCalendarQuery.MaxRangeDays)
            return Result<IReadOnlyList<TreatmentCalendarRow>>.Fail(ErrorCodes.AppointmentRangeTooWide);
        var kinds = q.Kinds is { Count: > 0 } ? q.Kinds : null;
        return Result<IReadOnlyList<TreatmentCalendarRow>>.Ok(await queries.GetCalendarAsync(
            q.From, q.To, kinds, q.ItemType, string.IsNullOrWhiteSpace(q.PetId) ? null : q.PetId,
            TreatmentCalendarQuery.MaxRows, ct));
    }
}

/// <summary>The treatment plans of one patient, newest first (bounded).</summary>
[RequiresPermission(PermissionCodes.VaccinationRead)]
public sealed record TreatmentPlansForPetQuery(string PetId, int Take = 50) : IQuery<IReadOnlyList<TreatmentPlanRow>>;

public sealed class TreatmentPlansForPetQueryHandler(ITreatmentPlanQueries queries)
    : IQueryHandler<TreatmentPlansForPetQuery, IReadOnlyList<TreatmentPlanRow>>
{
    public async Task<Result<IReadOnlyList<TreatmentPlanRow>>> HandleAsync(TreatmentPlansForPetQuery q,
        QueryContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q.PetId))
            return Result<IReadOnlyList<TreatmentPlanRow>>.Fail(ErrorCodes.ValidationFailed);
        return Result<IReadOnlyList<TreatmentPlanRow>>.Ok(
            await queries.GetPlansForPetAsync(q.PetId, Math.Clamp(q.Take, 1, 200), ct));
    }
}
