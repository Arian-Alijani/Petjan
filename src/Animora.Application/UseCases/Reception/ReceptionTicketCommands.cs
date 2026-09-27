using Animora.Application.Abstractions;
using Animora.Application.Auditing;
using Animora.Application.Commands;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Identity;
using Animora.Domain.Results;
using Animora.Domain.Scheduling;
using Animora.Domain.Time;

namespace Animora.Application.UseCases.Reception;

// ─────────────────────────────────────────────────────────────────────────────
// C02 reception ticket state changes. The queue is a DERIVED view: an
// appointment-sourced ticket has NO ReceptionQueueEntry until its first state
// change, when the handler lazily creates exactly one row (Source=Appointment,
// AppointmentId set). Walk-in tickets already own a row. Every handler resolves
// the composite ticket id through TicketRef.TryParse and maps a rejected domain
// transition to ErrorCodes.TicketStateInvalid.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Shared find-or-create + audit-category constants for the 5 reception ticket commands.</summary>
internal static class ReceptionTicketWork
{
    /// <summary>
    /// Returns the (tracked) queue row for <paramref name="ticket"/>, creating it lazily for an
    /// appointment ticket on its first touch. Fails <c>TICKET_NOT_FOUND</c> when the id resolves to no
    /// appointment/queue row.
    /// </summary>
    public static async Task<Result<ReceptionQueueEntry>> ResolveOrCreateAsync(
        TicketRef ticket,
        CommandContext context,
        IReceptionQueueRepository queue,
        IAppointmentRepository appointments,
        IClock clock,
        CancellationToken ct)
    {
        if (ticket.Kind == TicketKind.WalkIn)
        {
            var walkIn = await queue.FindAsync(ticket.Id, ct);
            return walkIn is null
                ? Result<ReceptionQueueEntry>.Fail(ErrorCodes.TicketNotFound, $"No walk-in ticket '{ticket.Id}'.")
                : Result<ReceptionQueueEntry>.Ok(walkIn);
        }

        var appointment = await appointments.FindAsync(ticket.Id, ct);
        if (appointment is null)
        {
            return Result<ReceptionQueueEntry>.Fail(
                ErrorCodes.TicketNotFound, $"No appointment ticket '{ticket.Id}'.");
        }

        var existing = await queue.FindByAppointmentAsync(appointment.Id, ct);
        if (existing is not null)
        {
            return Result<ReceptionQueueEntry>.Ok(existing);
        }

        var ticketNumber = await NextTicketNumberAsync(queue, clock, ct);
        var created = ReceptionQueueEntry.Create(
            context.TenantId, appointment.PetId, appointment.OwnerId, QueueSource.Appointment,
            ticketNumber, clock, appointmentId: appointment.Id, priority: ReceptionQueueEntry.PriorityNormal);
        queue.Add(created);
        return Result<ReceptionQueueEntry>.Ok(created);
    }

    /// <summary>Next per-local-day ticket number (count of today's rows + 1). Uses a UTC-day range.</summary>
    public static async Task<int> NextTicketNumberAsync(
        IReceptionQueueRepository queue, IClock clock, CancellationToken ct)
    {
        var today = clock.UtcNow.UtcDateTime.Date;
        var startUtc = new DateTimeOffset(today, TimeSpan.Zero);
        var count = await queue.CountForDayAsync(startUtc, startUtc.AddDays(1), ct);
        return count + 1;
    }

    /// <summary>Maps a failed domain transition to the C02 conflict code.</summary>
    public static Result<Unit> AsStateInvalid(Result moved) =>
        Result<Unit>.Fail(ErrorCodes.TicketStateInvalid, moved.Detail);
}

// ── Admit ────────────────────────────────────────────────────────────────────

/// <summary>Calls the patient (Waiting → Called), checking a booked appointment in first (C02).</summary>
[RequiresPermission(PermissionCodes.ReceptionManage)]
[Auditable(AuditCategory.Clinical, CommandTypes.ReceptionTicketAdmit)]
public sealed record AdmitTicketCommand(string TicketId)
    : ICommand<Unit>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.ReceptionTicketAdmit;
    public string AggregateTypeName => "ReceptionQueueEntry";
    public string? AggregateIdOrNull => null;
}

public sealed class AdmitTicketCommandHandler(
    IReceptionQueueRepository queue, IAppointmentRepository appointments, IClock clock)
    : ICommandHandler<AdmitTicketCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(AdmitTicketCommand c, CommandContext context, CancellationToken ct)
    {
        if (!TicketRef.TryParse(c.TicketId, out var ticket))
        {
            return Result<Unit>.Fail(ErrorCodes.TicketNotFound, $"Unparsable ticket '{c.TicketId}'.");
        }

        var resolve = await ReceptionTicketWork.ResolveOrCreateAsync(
            ticket, context, queue, appointments, clock, ct);
        if (resolve.IsFailure)
        {
            return Result<Unit>.Fail(resolve.Error, resolve.Detail);
        }

        var entry = resolve.Value;

        // Advance the backing appointment to CheckedIn on first admit so its lifecycle stays honest.
        if (ticket.Kind == TicketKind.Appointment)
        {
            var appointment = await appointments.RequireAsync(ticket.Id, ct);
            if (appointment.State is AppointmentState.Scheduled or AppointmentState.Confirmed)
            {
                appointment.CheckIn(clock);
            }
        }

        var doctorId = entry.DoctorUserId ?? context.UserId;
        var moved = entry.Call(doctorId, clock);
        return moved.IsFailure
            ? ReceptionTicketWork.AsStateInvalid(moved)
            : Result<Unit>.Ok(Unit.Value);
    }
}

// ── EnterVisit ─────────────────────────────────────────────────────────────────

/// <summary>
/// Moves a called ticket into the exam room (Called → InVisit). The queue's InVisit state is
/// visit-linked, so this requires a visit already started for the row (triage's
/// <c>StartVisitFromTicketCommand</c>); otherwise it is <c>TICKET_STATE_INVALID</c> (C02).
/// </summary>
[RequiresPermission(PermissionCodes.ReceptionManage)]
[Auditable(AuditCategory.Clinical, CommandTypes.ReceptionTicketEnter)]
public sealed record EnterVisitCommand(string TicketId)
    : ICommand<Unit>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.ReceptionTicketEnter;
    public string AggregateTypeName => "ReceptionQueueEntry";
    public string? AggregateIdOrNull => null;
}

public sealed class EnterVisitCommandHandler(
    IReceptionQueueRepository queue, IAppointmentRepository appointments, IClock clock)
    : ICommandHandler<EnterVisitCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(EnterVisitCommand c, CommandContext context, CancellationToken ct)
    {
        if (!TicketRef.TryParse(c.TicketId, out var ticket))
        {
            return Result<Unit>.Fail(ErrorCodes.TicketNotFound, $"Unparsable ticket '{c.TicketId}'.");
        }

        var resolve = await ReceptionTicketWork.ResolveOrCreateAsync(
            ticket, context, queue, appointments, clock, ct);
        if (resolve.IsFailure)
        {
            return Result<Unit>.Fail(resolve.Error, resolve.Detail);
        }

        var entry = resolve.Value;
        if (string.IsNullOrWhiteSpace(entry.VisitId))
        {
            return Result<Unit>.Fail(
                ErrorCodes.TicketStateInvalid,
                $"Ticket '{c.TicketId}' has no started visit; start the visit from triage first.");
        }

        var moved = entry.StartVisit(entry.VisitId);
        return moved.IsFailure
            ? ReceptionTicketWork.AsStateInvalid(moved)
            : Result<Unit>.Ok(Unit.Value);
    }
}

// ── LeaveQueue ─────────────────────────────────────────────────────────────────

/// <summary>Records that the patient left before being seen (→ Left) (C02).</summary>
[RequiresPermission(PermissionCodes.ReceptionManage)]
[Auditable(AuditCategory.Clinical, CommandTypes.ReceptionTicketLeave)]
public sealed record LeaveQueueCommand(string TicketId)
    : ICommand<Unit>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.ReceptionTicketLeave;
    public string AggregateTypeName => "ReceptionQueueEntry";
    public string? AggregateIdOrNull => null;
}

public sealed class LeaveQueueCommandHandler(
    IReceptionQueueRepository queue, IAppointmentRepository appointments, IClock clock)
    : ICommandHandler<LeaveQueueCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(LeaveQueueCommand c, CommandContext context, CancellationToken ct)
    {
        if (!TicketRef.TryParse(c.TicketId, out var ticket))
        {
            return Result<Unit>.Fail(ErrorCodes.TicketNotFound, $"Unparsable ticket '{c.TicketId}'.");
        }

        var resolve = await ReceptionTicketWork.ResolveOrCreateAsync(
            ticket, context, queue, appointments, clock, ct);
        if (resolve.IsFailure)
        {
            return Result<Unit>.Fail(resolve.Error, resolve.Detail);
        }

        var moved = resolve.Value.MarkLeft(clock);
        return moved.IsFailure
            ? ReceptionTicketWork.AsStateInvalid(moved)
            : Result<Unit>.Ok(Unit.Value);
    }
}

// ── MarkNoShow ─────────────────────────────────────────────────────────────────

/// <summary>
/// Marks an appointment ticket as a no-show. QueueState has no NoShow member, so the no-show lives
/// on the <c>Appointment</c> (→ NoShow); a present queue row (if any) is closed via
/// <c>MarkLeft</c>. Walk-ins have no appointment to no-show — they are just <c>MarkLeft</c> (C02).
/// </summary>
[RequiresPermission(PermissionCodes.ReceptionManage)]
[Auditable(AuditCategory.Clinical, CommandTypes.ReceptionTicketNoShow)]
public sealed record MarkNoShowCommand(string TicketId)
    : ICommand<Unit>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.ReceptionTicketNoShow;
    public string AggregateTypeName => "Appointment";
    public string? AggregateIdOrNull => null;
}

public sealed class MarkNoShowCommandHandler(
    IReceptionQueueRepository queue, IAppointmentRepository appointments, IClock clock)
    : ICommandHandler<MarkNoShowCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(MarkNoShowCommand c, CommandContext context, CancellationToken ct)
    {
        if (!TicketRef.TryParse(c.TicketId, out var ticket))
        {
            return Result<Unit>.Fail(ErrorCodes.TicketNotFound, $"Unparsable ticket '{c.TicketId}'.");
        }

        if (ticket.Kind == TicketKind.Appointment)
        {
            var appointment = await appointments.FindAsync(ticket.Id, ct);
            if (appointment is null)
            {
                return Result<Unit>.Fail(ErrorCodes.TicketNotFound, $"No appointment ticket '{ticket.Id}'.");
            }

            var noShow = appointment.MarkNoShow(clock);
            if (noShow.IsFailure)
            {
                return ReceptionTicketWork.AsStateInvalid(noShow);
            }

            var row = await queue.FindByAppointmentAsync(appointment.Id, ct);
            if (row is { State: QueueState.Waiting or QueueState.Called })
            {
                row.MarkLeft(clock);
            }

            return Result<Unit>.Ok(Unit.Value);
        }

        var walkIn = await queue.FindAsync(ticket.Id, ct);
        if (walkIn is null)
        {
            return Result<Unit>.Fail(ErrorCodes.TicketNotFound, $"No walk-in ticket '{ticket.Id}'.");
        }

        var moved = walkIn.MarkLeft(clock);
        return moved.IsFailure
            ? ReceptionTicketWork.AsStateInvalid(moved)
            : Result<Unit>.Ok(Unit.Value);
    }
}

// ── ToggleUrgent ───────────────────────────────────────────────────────────────

/// <summary>
/// Flips a ticket's triage priority between Normal and Emergency (C02). Creates the queue row lazily
/// for an appointment ticket so urgency (which an appointment cannot carry) can be recorded.
/// </summary>
[RequiresPermission(PermissionCodes.ReceptionManage)]
[Auditable(AuditCategory.Clinical, CommandTypes.ReceptionTicketUrgent)]
public sealed record ToggleTicketUrgentCommand(string TicketId)
    : ICommand<Unit>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.ReceptionTicketUrgent;
    public string AggregateTypeName => "ReceptionQueueEntry";
    public string? AggregateIdOrNull => null;
}

public sealed class ToggleTicketUrgentCommandHandler(
    IReceptionQueueRepository queue, IAppointmentRepository appointments, IClock clock)
    : ICommandHandler<ToggleTicketUrgentCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(
        ToggleTicketUrgentCommand c, CommandContext context, CancellationToken ct)
    {
        if (!TicketRef.TryParse(c.TicketId, out var ticket))
        {
            return Result<Unit>.Fail(ErrorCodes.TicketNotFound, $"Unparsable ticket '{c.TicketId}'.");
        }

        var resolve = await ReceptionTicketWork.ResolveOrCreateAsync(
            ticket, context, queue, appointments, clock, ct);
        if (resolve.IsFailure)
        {
            return Result<Unit>.Fail(resolve.Error, resolve.Detail);
        }

        var entry = resolve.Value;
        var next = entry.Priority >= ReceptionQueueEntry.PriorityEmergency
            ? ReceptionQueueEntry.PriorityNormal
            : ReceptionQueueEntry.PriorityEmergency;

        var moved = entry.SetPriority(next);
        return moved.IsFailure
            ? ReceptionTicketWork.AsStateInvalid(moved)
            : Result<Unit>.Ok(Unit.Value);
    }
}
