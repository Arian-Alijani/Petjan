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

// Local-only materialization: never journal this roster back to the server or create password verifiers.
[RequiresPermission(PermissionCodes.UsersManage)]
public sealed record RefreshVisitDoctorDirectoryCommand(string ServerTenantId, string ServerUserId,
    IReadOnlyList<VisitDoctorDirectoryEntry> Doctors) : ICommand<Unit>;

public sealed class RefreshVisitDoctorDirectoryCommandHandler(IVisitDoctorDirectoryWriter writer,
    IStaffMembershipRepository memberships) : ICommandHandler<RefreshVisitDoctorDirectoryCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(RefreshVisitDoctorDirectoryCommand command,
        CommandContext context, CancellationToken ct)
    {
        if (command.ServerTenantId != context.TenantId || command.ServerUserId != context.UserId)
            return Result<Unit>.Fail(ErrorCodes.TenantMismatch);
        var actor = await memberships.FindActiveAsync(context.TenantId, context.UserId, ct);
        if (actor?.Role != StaffRole.Manager)
            return Result<Unit>.Fail(ErrorCodes.PermissionDenied);
        var result = await writer.RefreshAsync(command.Doctors, context.UserId, ct);
        return result.IsFailure ? Result<Unit>.Fail(result.Error, result.Detail) : Result<Unit>.Ok(Unit.Value);
    }
}
