using Animora.Application.Abstractions;
using Animora.Application.Auditing;
using Animora.Application.Commands;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.Validation;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Identity;
using Animora.Domain.Patients;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Scheduling;
using Animora.Domain.Time;

namespace Animora.Application.UseCases.Reception;

/// <summary>
/// Registers a walk-in patient and enqueues them (C02). De-duplicates the owner by normalized phone
/// (an existing owner is reused, not cloned) and reuses that owner's like-named pet when present;
/// otherwise it creates the owner/pet. Returns the composite <c>wlk:</c> ticket id.
/// </summary>
[RequiresPermission(PermissionCodes.ReceptionManage)]
[Auditable(AuditCategory.Clinical, CommandTypes.ReceptionWalkInCreate)]
public sealed record CreateWalkInCommand(
    string PatientName,
    string OwnerName,
    string Phone,
    string? DoctorId,
    bool IsUrgent)
    : ICommand<string>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.ReceptionWalkInCreate;
    public string AggregateTypeName => "ReceptionQueueEntry";
    public string? AggregateIdOrNull => null;
}

public sealed class CreateWalkInCommandValidator : IValidator<CreateWalkInCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(CreateWalkInCommand c, CancellationToken ct)
    {
        var failures = new List<ValidationFailure>();
        if (Rules.Required(c.PatientName, nameof(c.PatientName)) is { } pet) failures.Add(pet);
        if (Rules.Required(c.OwnerName, nameof(c.OwnerName)) is { } owner) failures.Add(owner);
        // Phone drives de-duplication and is mandatory for a walk-in (WALKIN_PHONE_REQUIRED at handler).
        if (Rules.Required(c.Phone, nameof(c.Phone)) is { } phone) failures.Add(phone);
        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

public sealed class CreateWalkInCommandHandler(
    IOwnerRepository owners,
    IPetRepository pets,
    IReceptionQueueRepository queue,
    IStaffMembershipRepository memberships,
    IClock clock)
    : ICommandHandler<CreateWalkInCommand, string>
{
    public async Task<Result<string>> HandleAsync(CreateWalkInCommand c, CommandContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(c.Phone))
        {
            return Result<string>.Fail(ErrorCodes.WalkInPhoneRequired, "A walk-in requires a contact phone.");
        }

        // An addressed doctor must be an active staff member (DOCTOR_NOT_AVAILABLE).
        if (!string.IsNullOrWhiteSpace(c.DoctorId))
        {
            var membership = await memberships.FindActiveAsync(context.TenantId, c.DoctorId, ct);
            if (membership is null || membership.Role != StaffRole.Doctor)
            {
                return Result<string>.Fail(
                    ErrorCodes.DoctorNotAvailable, $"Doctor '{c.DoctorId}' is not an active doctor.");
            }
        }

        var normalizedPhone = Owner.NormalizePhone(c.Phone);
        var owner = await owners.FindByPhoneAsync(normalizedPhone, ct);
        if (owner is null)
        {
            owner = Owner.Register(context.TenantId, c.OwnerName, c.Phone, clock,
                id: IdGenerator.ForOperation(context.OperationId, 1, clock.UtcNow));
            owners.Add(owner);
        }

        var pet = await pets.FindByOwnerAndNameAsync(owner.Id, c.PatientName, ct);
        if (pet is null)
        {
            pet = Pet.Register(context.TenantId, owner.Id, c.PatientName, PetSpecies.Other, clock,
                id: IdGenerator.ForOperation(context.OperationId, 2, clock.UtcNow));
            pets.Add(pet);
        }

        var ticketNumber = await ReceptionTicketWork.NextTicketNumberAsync(queue, clock, ct);
        var priority = c.IsUrgent ? ReceptionQueueEntry.PriorityEmergency : ReceptionQueueEntry.PriorityNormal;
        var entry = ReceptionQueueEntry.Create(
            context.TenantId, pet.Id, owner.Id, QueueSource.WalkIn, ticketNumber, clock, priority: priority,
            id: IdGenerator.ForOperation(context.OperationId, 0, clock.UtcNow));
        queue.Add(entry);

        // Attribute the calling doctor immediately when one was chosen (Waiting → Called).
        if (!string.IsNullOrWhiteSpace(c.DoctorId))
        {
            entry.Call(c.DoctorId, clock);
        }

        return Result<string>.Ok(TicketRef.FromWalkIn(entry.Id).Format());
    }
}
