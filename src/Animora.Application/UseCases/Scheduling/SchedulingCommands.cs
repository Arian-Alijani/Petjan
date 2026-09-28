using Animora.Application.Abstractions;
using Animora.Application.Auditing;
using Animora.Application.Commands;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.Time;
using Animora.Application.UseCases.Reception;
using Animora.Application.Validation;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Identity;
using Animora.Domain.Patients;
using Animora.Domain.Results;
using Animora.Domain.Scheduling;
using Animora.Domain.Time;

namespace Animora.Application.UseCases.Scheduling;

internal static class BookingWork
{
    internal static async Task<Result> EnsureSlotAsync(IAppointmentQueries queries, IClinicTimeZoneProvider zones,
        DateOnly day, int minute, int duration, string doctorId, string? except, CancellationToken ct)
    {
        if (minute < 0 || minute + duration > 1440 || duration < Appointment.MinDurationMinutes ||
            duration > Appointment.MaxDurationMinutes || duration % Appointment.DurationStepMinutes != 0)
            return Result.Fail(ErrorCodes.AppointmentDurationInvalid);
        var hours = await queries.GetDoctorWorkingHoursAsync(doctorId, ct);
        var weekdayBit = ((int)day.DayOfWeek + 1) % 7;
        if (hours is null || minute < hours.Value.WorkStartMinute ||
            minute + duration > hours.Value.WorkEndMinute ||
            (hours.Value.WorkDaysMask & (1 << weekdayBit)) == 0)
            return Result.Fail(ErrorCodes.DoctorOutsideWorkingHours);
        var start = ClinicCalendar.ToUtcTicks(day, minute, zones.GetClinicTimeZone());
        var busy = await queries.GetDoctorSlotsAsync(doctorId, start, start + TimeSpan.FromMinutes(duration).Ticks, except, ct);
        return SlotConflictPolicy.EnsureFree(new AppointmentSlot(new DateTimeOffset(start, TimeSpan.Zero), duration), doctorId, busy);
    }
}

[RequiresPermission(PermissionCodes.AppointmentsBook)]
[Auditable(AuditCategory.Clinical, CommandTypes.SchedulingAppointmentBook)]
public sealed record BookAppointmentCommand(DateOnly Date, int StartMinutes, int DurationMinutes,
    string TypeId, string DoctorId, string PetId, string OwnerId, string? Note)
    : ICommand<string>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.SchedulingAppointmentBook;
    public string AggregateTypeName => "Appointment";
    public string? AggregateIdOrNull => null;
}
public sealed class BookAppointmentCommandValidator : IValidator<BookAppointmentCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(BookAppointmentCommand c, CancellationToken ct)
    {
        var failures = new List<ValidationFailure>();
        foreach (var (value, field) in new[] { (c.PetId, nameof(c.PetId)), (c.OwnerId, nameof(c.OwnerId)),
            (c.TypeId, nameof(c.TypeId)), (c.DoctorId, nameof(c.DoctorId)) })
            if (Rules.Required(value, field) is { } failure) failures.Add(failure);
        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}
public sealed class BookAppointmentCommandHandler(IAppointmentRepository repository, IAppointmentQueries queries,
    IClinicTimeZoneProvider zones, IClock clock) : ICommandHandler<BookAppointmentCommand, string>
{
    public async Task<Result<string>> HandleAsync(BookAppointmentCommand c, CommandContext context, CancellationToken ct)
    {
        if (!AppointmentTypeCatalog.TryGet(c.TypeId, out _))
            return Result<string>.Fail(ErrorCodes.ValidationFailed, "Unknown appointment type.");
        var free = await BookingWork.EnsureSlotAsync(queries, zones, c.Date, c.StartMinutes,
            c.DurationMinutes, c.DoctorId, null, ct);
        if (free.IsFailure) return Result<string>.Fail(free.Error, free.Detail);
        var start = new DateTimeOffset(ClinicCalendar.ToUtcTicks(c.Date, c.StartMinutes,
            zones.GetClinicTimeZone()), TimeSpan.Zero);
        var booked = Appointment.Book(context.TenantId, c.PetId, c.OwnerId, c.TypeId, start,
            c.DurationMinutes, AppointmentSource.Phone, context.UserId, clock, c.DoctorId, c.Note);
        if (booked.IsFailure) return Result<string>.Fail(booked.Error, booked.Detail);
        repository.Add(booked.Value!);
        return Result<string>.Ok(booked.Value!.Id);
    }
}

[RequiresPermission(PermissionCodes.AppointmentsMove)]
[Auditable(AuditCategory.Clinical, CommandTypes.SchedulingAppointmentMove)]
public sealed record RescheduleAppointmentCommand(string AppointmentId, DateOnly Date, int StartMinutes, int DurationMinutes)
    : ICommand<Unit>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.SchedulingAppointmentMove;
    public string AggregateTypeName => "Appointment";
    public string? AggregateIdOrNull => AppointmentId;
}
public sealed class RescheduleAppointmentCommandHandler(IAppointmentRepository repository, IAppointmentQueries queries,
    IClinicTimeZoneProvider zones, IClock clock) : ICommandHandler<RescheduleAppointmentCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(RescheduleAppointmentCommand c, CommandContext context, CancellationToken ct)
    {
        var appointment = await repository.FindAsync(c.AppointmentId, ct);
        if (appointment is null) return Result<Unit>.Fail(ErrorCodes.AppointmentNotFound);
        if (appointment.State >= AppointmentState.InVisit) return Result<Unit>.Fail(ErrorCodes.AppointmentNotMovable);
        if (c.StartMinutes < 0 || c.StartMinutes + c.DurationMinutes > 1440 ||
            c.DurationMinutes < Appointment.MinDurationMinutes ||
            c.DurationMinutes > Appointment.MaxDurationMinutes ||
            c.DurationMinutes % Appointment.DurationStepMinutes != 0)
            return Result<Unit>.Fail(ErrorCodes.AppointmentDurationInvalid);
        if (!string.IsNullOrEmpty(appointment.DoctorUserId))
        {
            var free = await BookingWork.EnsureSlotAsync(queries, zones, c.Date, c.StartMinutes,
                c.DurationMinutes, appointment.DoctorUserId, appointment.Id, ct);
            if (free.IsFailure) return Result<Unit>.Fail(free.Error, free.Detail);
        }
        var moved = appointment.Move(new DateTimeOffset(ClinicCalendar.ToUtcTicks(c.Date,
            c.StartMinutes, zones.GetClinicTimeZone()), TimeSpan.Zero), c.DurationMinutes, clock);
        return moved.IsFailure ? Result<Unit>.Fail(moved.Error, moved.Detail) : Result<Unit>.Ok(Unit.Value);
    }
}

[RequiresPermission(PermissionCodes.AppointmentsCancel)]
[Auditable(AuditCategory.Clinical, CommandTypes.SchedulingAppointmentCancel)]
public sealed record CancelAppointmentCommand(string AppointmentId, string? Reason) : ICommand<Unit>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.SchedulingAppointmentCancel;
    public string AggregateTypeName => "Appointment";
    public string? AggregateIdOrNull => AppointmentId;
}
public sealed class CancelAppointmentCommandHandler(IAppointmentRepository repository, IClock clock)
    : ICommandHandler<CancelAppointmentCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(CancelAppointmentCommand c, CommandContext context, CancellationToken ct)
    {
        var appointment = await repository.FindAsync(c.AppointmentId, ct);
        if (appointment is null) return Result<Unit>.Fail(ErrorCodes.AppointmentNotFound);
        if (appointment.State == AppointmentState.CheckedIn) return Result<Unit>.Fail(ErrorCodes.AppointmentStateInvalid);
        var cancelled = appointment.Cancel(string.IsNullOrWhiteSpace(c.Reason) ? "cancelled" : c.Reason, clock);
        return cancelled.IsFailure ? Result<Unit>.Fail(ErrorCodes.AppointmentStateInvalid, cancelled.Detail)
            : Result<Unit>.Ok(Unit.Value);
    }
}

[RequiresPermission(PermissionCodes.AppointmentsBook)]
[Auditable(AuditCategory.Clinical, CommandTypes.SchedulingAppointmentCheckin)]
public sealed record CheckInAppointmentCommand(string AppointmentId) : ICommand<Unit>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.SchedulingAppointmentCheckin;
    public string AggregateTypeName => "Appointment";
    public string? AggregateIdOrNull => AppointmentId;
}
public sealed class CheckInAppointmentCommandHandler(IAppointmentRepository repository,
    IReceptionQueueRepository queue, IClock clock) : ICommandHandler<CheckInAppointmentCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(CheckInAppointmentCommand c, CommandContext context, CancellationToken ct)
    {
        var appointment = await repository.FindAsync(c.AppointmentId, ct);
        if (appointment is null) return Result<Unit>.Fail(ErrorCodes.AppointmentNotFound);
        if (appointment.State is not (AppointmentState.Scheduled or AppointmentState.Confirmed or AppointmentState.CheckedIn))
            return Result<Unit>.Fail(ErrorCodes.AppointmentStateInvalid);
        if (appointment.State != AppointmentState.CheckedIn)
        {
            var checkedIn = appointment.CheckIn(clock);
            if (checkedIn.IsFailure) return Result<Unit>.Fail(ErrorCodes.AppointmentStateInvalid);
        }
        var resolved = await QueueEntryResolver.ResolveOrCreateForAppointmentAsync(
            appointment, context, queue, clock, ct);
        if (resolved.IsFailure) return Result<Unit>.Fail(resolved.Error, resolved.Detail);
        return Result<Unit>.Ok(Unit.Value);
    }
}

[RequiresPermission(PermissionCodes.AppointmentsBook)]
[Auditable(AuditCategory.Clinical, CommandTypes.SchedulingBookingPatientCreate)]
public sealed record CreateBookingPatientCommand(string Name, string Kind, string OwnerName, string Phone)
    : ICommand<string>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.SchedulingBookingPatientCreate;
    public string AggregateTypeName => "Pet";
    public string? AggregateIdOrNull => null;
}
public sealed class CreateBookingPatientCommandValidator : IValidator<CreateBookingPatientCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(CreateBookingPatientCommand c, CancellationToken ct)
    {
        var failures = new List<ValidationFailure>();
        foreach (var (value, field) in new[] { (c.Name, nameof(c.Name)), (c.OwnerName, nameof(c.OwnerName)),
            (c.Phone, nameof(c.Phone)) })
            if (Rules.Required(value, field) is { } failure) failures.Add(failure);
        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}
public sealed class CreateBookingPatientCommandHandler(IOwnerRepository owners, IPetRepository pets,
    IClock clock) : ICommandHandler<CreateBookingPatientCommand, string>
{
    public async Task<Result<string>> HandleAsync(CreateBookingPatientCommand c, CommandContext context, CancellationToken ct)
    {
        // TransactionBehavior already encloses this handler and its journal write in one UoW.
        var owner = await owners.FindByPhoneAsync(Owner.NormalizePhone(c.Phone), ct);
        if (owner is null)
        {
            owner = Owner.Register(context.TenantId, c.OwnerName, c.Phone, clock);
            owners.Add(owner);
        }
        var species = Enum.TryParse<PetSpecies>(c.Kind, true, out var parsed) ? parsed : PetSpecies.Other;
        var pet = Pet.Register(context.TenantId, owner.Id, c.Name, species, clock);
        pets.Add(pet);
        return Result<string>.Ok(pet.Id);
    }
}
