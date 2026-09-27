using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.UseCases.Visits.ReadModels;
using Animora.Domain.Identity;
using Animora.Domain.Results;

namespace Animora.Application.UseCases.Visits;

[RequiresPermission(PermissionCodes.VisitsRead)]
public sealed record TriageDoctorRailQuery(DateOnly LocalDay) : IQuery<IReadOnlyList<DoctorRailReadModel>>;

public sealed class TriageDoctorRailQueryHandler(ITriageQueries queries)
    : IQueryHandler<TriageDoctorRailQuery, IReadOnlyList<DoctorRailReadModel>>
{
    public async Task<Result<IReadOnlyList<DoctorRailReadModel>>> HandleAsync(TriageDoctorRailQuery query,
        QueryContext context, CancellationToken ct)
    {
        var start = new DateTimeOffset(query.LocalDay.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        return Result<IReadOnlyList<DoctorRailReadModel>>.Ok(await queries.GetDoctorRailAsync(start, start.AddDays(1), ct));
    }
}

[RequiresPermission(PermissionCodes.VisitsRead)]
public sealed record BookedForDoctorQuery(string DoctorId, DateOnly LocalDay)
    : IQuery<IReadOnlyList<BookedAppointmentReadModel>>;

public sealed class BookedForDoctorQueryHandler(ITriageQueries queries)
    : IQueryHandler<BookedForDoctorQuery, IReadOnlyList<BookedAppointmentReadModel>>
{
    public async Task<Result<IReadOnlyList<BookedAppointmentReadModel>>> HandleAsync(BookedForDoctorQuery query,
        QueryContext context, CancellationToken ct)
    {
        var start = new DateTimeOffset(query.LocalDay.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        return Result<IReadOnlyList<BookedAppointmentReadModel>>.Ok(
            await queries.GetBookedForDoctorAsync(query.DoctorId, start, start.AddDays(1), ct));
    }
}

[RequiresPermission(PermissionCodes.VisitsRead)]
public sealed record TriagePatientsQuery(string? Search) : IQuery<IReadOnlyList<TriagePatientReadModel>>;

public sealed class TriagePatientsQueryHandler(ITriageQueries queries)
    : IQueryHandler<TriagePatientsQuery, IReadOnlyList<TriagePatientReadModel>>
{
    public async Task<Result<IReadOnlyList<TriagePatientReadModel>>> HandleAsync(TriagePatientsQuery query,
        QueryContext context, CancellationToken ct) =>
        Result<IReadOnlyList<TriagePatientReadModel>>.Ok(await queries.GetRecentPatientsAsync(query.Search, ct));
}
