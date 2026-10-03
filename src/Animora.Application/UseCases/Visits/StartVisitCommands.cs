using Animora.Application.Abstractions;
using Animora.Application.Auditing;
using Animora.Application.Commands;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.UseCases.Reception;
using Animora.Application.UseCases.Visits.ReadModels;
using Animora.Application.Validation;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Clinical;
using Animora.Domain.Identity;
using Animora.Domain.Patients;
using Animora.Domain.Results;
using Animora.Domain.Scheduling;
using Animora.Domain.Time;

namespace Animora.Application.UseCases.Visits;

[RequiresPermission(PermissionCodes.VisitsWrite)]
[Auditable(AuditCategory.Clinical, CommandTypes.VisitsVisitStart)]
public sealed record StartVisitFromTicketCommand(string TicketId)
    : ICommand<EncounterDraftReadModel>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.VisitsVisitStart;
    public string AggregateTypeName => "Visit";
    public string? AggregateIdOrNull => null;
}

public sealed class StartVisitFromTicketCommandValidator : IValidator<StartVisitFromTicketCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(StartVisitFromTicketCommand c, CancellationToken ct) =>
        ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(
            Rules.Required(c.TicketId, nameof(c.TicketId)) is { } failure ? [failure] : []);
}

public sealed class StartVisitFromTicketCommandHandler(
    IReceptionQueueRepository queue, IAppointmentRepository appointments, IVisitRepository visits,
    IPetRepository pets, IOwnerRepository owners, IStaffMembershipRepository memberships,
    IReceptionQueueQueries queries, IClock clock)
    : ICommandHandler<StartVisitFromTicketCommand, EncounterDraftReadModel>
{
    public async Task<Result<EncounterDraftReadModel>> HandleAsync(
        StartVisitFromTicketCommand c, CommandContext context, CancellationToken ct)
    {
        if (!TicketRef.TryParse(c.TicketId, out var ticket))
            return Result<EncounterDraftReadModel>.Fail(ErrorCodes.TicketNotFound);

        var appointment = ticket.Kind == TicketKind.Appointment ? await appointments.FindAsync(ticket.Id, ct) : null;
        var entry = ticket.Kind == TicketKind.Appointment
            ? appointment is null ? null : await queue.FindByAppointmentAsync(appointment.Id, ct)
            : await queue.FindAsync(ticket.Id, ct);
        if (appointment is null && entry is null)
            return Result<EncounterDraftReadModel>.Fail(ErrorCodes.TicketNotFound);

        var doctorId = appointment?.DoctorUserId ?? entry?.DoctorUserId;
        if (string.IsNullOrWhiteSpace(doctorId))
            return Result<EncounterDraftReadModel>.Fail(ErrorCodes.DoctorNotAvailable);
        var doctor = await memberships.FindActiveAsync(context.TenantId, doctorId, ct);
        if (doctor is null || doctor.Role != StaffRole.Doctor)
            return Result<EncounterDraftReadModel>.Fail(ErrorCodes.DoctorNotAvailable);

        var pet = await pets.FindAsync((appointment?.PetId ?? entry!.PetId), ct);
        var owner = await owners.FindAsync((appointment?.OwnerId ?? entry!.OwnerId), ct);
        if (pet is null || owner is null) return Result<EncounterDraftReadModel>.Fail(ErrorCodes.TicketNotFound);
        var existingId = entry?.VisitId ?? appointment?.VisitId;
        if (existingId is not null)
        {
            var existing = await visits.FindAsync(existingId, ct);
            if (existing is { State: VisitState.InProgress } && entry is not null)
                return Result<EncounterDraftReadModel>.Ok(await DraftAsync(existing, ticket.Format(), appointment, entry, pet, owner, queries, ct));
            return Result<EncounterDraftReadModel>.Fail(ErrorCodes.TicketStateInvalid);
        }
        if (appointment is { State: AppointmentState.NoShow or AppointmentState.Cancelled or AppointmentState.Completed }
            || entry is { State: QueueState.Left or QueueState.Done or QueueState.InVisit })
            return Result<EncounterDraftReadModel>.Fail(ErrorCodes.TicketStateInvalid);

        if (appointment is not null && appointment.State is AppointmentState.Scheduled or AppointmentState.Confirmed)
        {
            var checkedIn = appointment.CheckIn(clock);
            if (checkedIn.IsFailure) return Result<EncounterDraftReadModel>.Fail(ErrorCodes.TicketStateInvalid);
        }
        if (entry is null)
        {
            var number = await ReceptionTicketWork.NextTicketNumberAsync(queue, clock, ct);
            entry = ReceptionQueueEntry.Create(context.TenantId, pet.Id, owner.Id, QueueSource.Appointment,
                number, clock, appointmentId: appointment!.Id);
            queue.Add(entry);
        }
        if (entry.State == QueueState.Waiting)
        {
            var called = entry.Call(doctorId, clock);
            if (called.IsFailure) return Result<EncounterDraftReadModel>.Fail(ErrorCodes.TicketStateInvalid);
        }
        var visit = Visit.Open(context.TenantId, pet.Id, owner.Id, doctorId, VisitReason.Checkup, clock,
            appointment?.Id);
        var started = visit.Start();
        if (started.IsFailure) return Result<EncounterDraftReadModel>.Fail(started.Error);
        var moved = entry.StartVisit(visit.Id);
        if (moved.IsFailure) return Result<EncounterDraftReadModel>.Fail(ErrorCodes.TicketStateInvalid);
        if (appointment is not null)
        {
            var linked = appointment.StartVisit(visit.Id);
            if (linked.IsFailure) return Result<EncounterDraftReadModel>.Fail(ErrorCodes.TicketStateInvalid);
        }
        visits.Add(visit);
        return Result<EncounterDraftReadModel>.Ok(await DraftAsync(visit, ticket.Format(), appointment, entry, pet, owner, queries, ct));
    }

    internal static async Task<EncounterDraftReadModel> DraftAsync(Visit visit, string ticketId,
        Appointment? appointment, ReceptionQueueEntry entry, Pet pet, Owner owner,
        IReceptionQueueQueries queries, CancellationToken ct)
    {
        var doctors = await queries.GetDoctorsAsync(ct);
        var doctor = doctors.FirstOrDefault(d => d.Id == visit.DoctorUserId);
        return new EncounterDraftReadModel(visit.Id, ticketId, visit.DoctorUserId,
            doctor?.Name ?? string.Empty, doctor?.Specialty ?? string.Empty,
            pet.Id, pet.Name, pet.Species.ToString(), owner.FullName, owner.PhonePrimary,
            appointment?.AppointmentTypeCode ?? "CHECKUP", entry.Priority > 0,
            appointment is null, (appointment?.StartUtc ?? entry.EnqueuedUtc).Hour * 60 +
                (appointment?.StartUtc ?? entry.EnqueuedUtc).Minute,
            appointment?.DurationMinutes ?? 30, visit.ChiefComplaint ?? string.Empty, ticketId);
    }
}

[RequiresPermission(PermissionCodes.VisitsWrite)]
[Auditable(AuditCategory.Clinical, CommandTypes.VisitsVisitStart)]
public sealed record StartNewVisitCommand(string DoctorId, string PatientName, string PatientKind,
    string OwnerName, string OwnerPhone, string TypeId, bool IsUrgent, string Note)
    : ICommand<EncounterDraftReadModel>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.VisitsVisitStart;
    public string AggregateTypeName => "Visit";
    public string? AggregateIdOrNull => null;
}

public sealed class StartNewVisitCommandValidator : IValidator<StartNewVisitCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(StartNewVisitCommand c, CancellationToken ct)
    {
        var failures = new List<ValidationFailure>();
        foreach (var (value, field) in new[] { (c.DoctorId, nameof(c.DoctorId)),
                     (c.PatientName, nameof(c.PatientName)), (c.OwnerName, nameof(c.OwnerName)),
                     (c.OwnerPhone, nameof(c.OwnerPhone)) })
            if (Rules.Required(value, field) is { } failure) failures.Add(failure);
        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

public sealed class StartNewVisitCommandHandler(
    IReceptionQueueRepository queue, IVisitRepository visits, IPetRepository pets,
    IOwnerRepository owners, IStaffMembershipRepository memberships, IReceptionQueueQueries queries, IClock clock)
    : ICommandHandler<StartNewVisitCommand, EncounterDraftReadModel>
{
    public async Task<Result<EncounterDraftReadModel>> HandleAsync(StartNewVisitCommand c, CommandContext context, CancellationToken ct)
    {
        var doctor = await memberships.FindActiveAsync(context.TenantId, c.DoctorId, ct);
        if (doctor is null || doctor.Role != StaffRole.Doctor)
            return Result<EncounterDraftReadModel>.Fail(ErrorCodes.DoctorNotAvailable);
        if (string.IsNullOrWhiteSpace(c.OwnerPhone))
            return Result<EncounterDraftReadModel>.Fail(ErrorCodes.WalkInPhoneRequired);
        var owner = await owners.FindByPhoneAsync(Owner.NormalizePhone(c.OwnerPhone), ct);
        if (owner is null)
        {
            owner = Owner.Register(context.TenantId, c.OwnerName, c.OwnerPhone, clock);
            owners.Add(owner);
        }
        // Reuse an existing animal only when it is an unambiguous single same-named match of the SAME species;
        // otherwise register a distinct record so two animals' histories never merge (V08). A code/chip
        // selector for explicit disambiguation is a UI follow-up.
        var species = PetSpeciesParser.Parse(c.PatientKind);
        var sameName = await pets.ListByOwnerAndNameAsync(owner.Id, c.PatientName, ct);
        var pet = sameName.Count == 1 && sameName[0].Species == species ? sameName[0] : null;
        if (pet is null)
        {
            pet = Pet.Register(context.TenantId, owner.Id, c.PatientName, species, clock);
            pets.Add(pet);
        }
        var number = await ReceptionTicketWork.NextTicketNumberAsync(queue, clock, ct);
        var entry = ReceptionQueueEntry.Create(context.TenantId, pet.Id, owner.Id, QueueSource.WalkIn,
            number, clock, priority: c.IsUrgent ? ReceptionQueueEntry.PriorityEmergency : ReceptionQueueEntry.PriorityNormal);
        var called = entry.Call(c.DoctorId, clock);
        if (called.IsFailure) return Result<EncounterDraftReadModel>.Fail(ErrorCodes.TicketStateInvalid);
        var visit = Visit.Open(context.TenantId, pet.Id, owner.Id, c.DoctorId, VisitReason.Checkup, clock,
            chiefComplaint: c.Note);
        var started = visit.Start();
        if (started.IsFailure) return Result<EncounterDraftReadModel>.Fail(started.Error);
        var moved = entry.StartVisit(visit.Id);
        if (moved.IsFailure) return Result<EncounterDraftReadModel>.Fail(ErrorCodes.TicketStateInvalid);
        queue.Add(entry);
        visits.Add(visit);
        var ticketId = TicketRef.FromWalkIn(entry.Id).Format();
        var draft = await StartVisitFromTicketCommandHandler.DraftAsync(visit, ticketId, null, entry, pet, owner, queries, ct);
        return Result<EncounterDraftReadModel>.Ok(draft with { TypeId = c.TypeId });
    }
}

/// <summary>Maps the free-text patient kind entered at triage (Persian or English) to a <see cref="PetSpecies"/>.</summary>
internal static class PetSpeciesParser
{
    public static PetSpecies Parse(string? kind)
    {
        var k = (kind ?? string.Empty).Trim().ToLowerInvariant();
        if (k.Length == 0) return PetSpecies.Other;
        if (Enum.TryParse<PetSpecies>(k, ignoreCase: true, out var exact) && Enum.IsDefined(exact)) return exact;
        if (Has(k, "سگ", "dog", "puppy", "ژرمن", "هاسکی", "پاگ", "گلدن", "تریر", "پودل")) return PetSpecies.Dog;
        if (Has(k, "گربه", "cat", "kitten", "پرشین", "اسکاتیش", "بنگال", "رگدال")) return PetSpecies.Cat;
        if (Has(k, "پرنده", "bird", "طوطی", "کاناری", "مرغ", "عروس هلندی", "کوکتیل")) return PetSpecies.Bird;
        if (Has(k, "جونده", "rodent", "خرگوش", "همستر", "خوکچه", "موش", "rabbit", "hamster")) return PetSpecies.Rodent;
        if (Has(k, "خزنده", "reptile", "مار", "لاک‌پشت", "لاکپشت", "مارمولک", "ایگوانا")) return PetSpecies.Reptile;
        return PetSpecies.Other;
    }

    private static bool Has(string value, params string[] needles) => needles.Any(value.Contains);
}
