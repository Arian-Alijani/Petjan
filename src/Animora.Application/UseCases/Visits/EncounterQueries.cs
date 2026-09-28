using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.UseCases.Reception;
using Animora.Application.UseCases.Visits.ReadModels;
using Animora.Domain.Clinical;
using Animora.Domain.Identity;
using Animora.Domain.Results;

namespace Animora.Application.UseCases.Visits;

public sealed record EncounterEditReadModel(EncounterDraftReadModel Draft, PetVitals? Vitals,
    string History, string Exam, string Diagnosis, string Plan,
    IReadOnlyList<VisitServiceLine> Lines);

public sealed record EncounterSummaryReadModel(string EncounterId, string PatientName, string PatientKind,
    string DoctorId, string DoctorShortName, long Subtotal, long Tax, long Total, DateTimeOffset CompletedUtc);

public sealed record RxCatalogItemReadModel(string Kind, string Id, string Title, long Price);

[RequiresPermission(PermissionCodes.VisitsRead)]
public sealed record EncounterDraftQuery(string VisitId) : IQuery<EncounterEditReadModel>;

public sealed class EncounterDraftQueryHandler(IVisitRepository visits, IPetRepository pets,
    IOwnerRepository owners, IAppointmentRepository appointments, IReceptionQueueRepository queue,
    ITriageQueries triage) : IQueryHandler<EncounterDraftQuery, EncounterEditReadModel>
{
    public async Task<Result<EncounterEditReadModel>> HandleAsync(EncounterDraftQuery query,
        QueryContext context, CancellationToken ct)
    {
        var visit = await visits.FindAsync(query.VisitId, ct);
        if (visit is null) return Result<EncounterEditReadModel>.Fail(ErrorCodes.VisitNotFound);
        var pet = await pets.FindAsync(visit.PetId, ct);
        var owner = await owners.FindAsync(visit.OwnerId, ct);
        if (pet is null || owner is null) return Result<EncounterEditReadModel>.Fail(ErrorCodes.VisitNotFound);
        var appointment = visit.AppointmentId is { } appointmentId
            ? await appointments.FindAsync(appointmentId, ct) : null;
        var entry = await queue.FindByVisitAsync(visit.Id, ct);
        var ticketId = visit.AppointmentId is { } apt
            ? TicketRef.FromAppointment(apt).Format()
            : entry is { } walkIn ? TicketRef.FromWalkIn(walkIn.Id).Format() : string.Empty;
        var doctors = await triage.GetDoctorRailAsync(visit.StartedUtc.AddDays(-1),
            visit.StartedUtc.AddDays(1), ct);
        var doctor = doctors.FirstOrDefault(d => d.Id == visit.DoctorUserId);
        var draft = new EncounterDraftReadModel(visit.Id, ticketId, visit.DoctorUserId,
            doctor?.Name ?? string.Empty, doctor?.Specialty ?? string.Empty,
            pet.Id, pet.Name, pet.Species.ToString(), owner.FullName, owner.PhonePrimary,
            appointment?.AppointmentTypeCode ?? "CHECKUP", entry?.Priority > 0,
            appointment is null, (appointment?.StartUtc ?? visit.StartedUtc).Hour * 60 +
                (appointment?.StartUtc ?? visit.StartedUtc).Minute,
            appointment?.DurationMinutes ?? 30, visit.ChiefComplaint ?? string.Empty, ticketId);
        var (history, exam) = EncounterNoteSections.Split(visit.DoctorNotes);
        return Result<EncounterEditReadModel>.Ok(new EncounterEditReadModel(draft, visit.Vitals,
            history, exam, visit.Diagnosis ?? string.Empty, visit.TreatmentPlan ?? string.Empty,
            visit.ServiceLines.ToArray()));
    }
}

[RequiresPermission(PermissionCodes.VisitsRead)]
public sealed record RxCatalogQuery : IQuery<IReadOnlyList<RxCatalogItemReadModel>>;

public sealed class RxCatalogQueryHandler(IRxCatalogQueries catalog)
    : IQueryHandler<RxCatalogQuery, IReadOnlyList<RxCatalogItemReadModel>>
{
    public async Task<Result<IReadOnlyList<RxCatalogItemReadModel>>> HandleAsync(RxCatalogQuery query,
        QueryContext context, CancellationToken ct) =>
        Result<IReadOnlyList<RxCatalogItemReadModel>>.Ok(await catalog.GetAsync(ct));
}
