using Animora.Application.Abstractions;
using Animora.Application.Auditing;
using Animora.Application.Commands;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.Validation;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Identity;
using Animora.Domain.Patients;
using Animora.Domain.Results;

namespace Animora.Application.UseCases.Patients;

// Delete = soft-delete (tombstone). `SyncableEntity.Deleted = true` is hidden by the global
// "SoftDelete" EF query filter and `UpdatedAt` is auto-stamped on SaveChanges, so the tombstone
// propagates to sync for free. Mirrors `DeleteProductCommand` (Inventory).

/// <summary>Soft-deletes a single pet. Reuses the pet-edit permission (no catalog churn).</summary>
[RequiresPermission(PermissionCodes.PatientsPetEdit)]
[Auditable(AuditCategory.Clinical, "patients.pet.delete")]
public sealed record DeletePetCommand(string PetId) : ICommand<Unit>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.PatientsPetDelete;
    public string AggregateTypeName => "Pet";
    public string? AggregateIdOrNull => PetId;
}

public sealed class DeletePetCommandValidator : IValidator<DeletePetCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(DeletePetCommand c, CancellationToken ct)
    {
        var failures = new List<ValidationFailure>();
        if (Rules.Required(c.PetId, nameof(c.PetId)) is { } id) failures.Add(id);
        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

public sealed class DeletePetCommandHandler(IPetRepository pets) : ICommandHandler<DeletePetCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(DeletePetCommand c, CommandContext context, CancellationToken ct)
    {
        var pet = await pets.FindAsync(c.PetId, ct);
        if (pet is null) return Result<Unit>.Fail(ErrorCodes.PetNotFound);
        pet.Deleted = true;
        return Result<Unit>.Ok(Unit.Value);
    }
}

/// <summary>
/// Soft-deletes an owner and cascades the tombstone to every live pet they own, in one unit of
/// work — so no orphan pets linger and the user never has to delete pets first.
/// Reuses the owner-edit permission (no catalog churn).
/// </summary>
[RequiresPermission(PermissionCodes.PatientsOwnerEdit)]
[Auditable(AuditCategory.Clinical, "patients.owner.delete")]
public sealed record DeleteOwnerCommand(string OwnerId) : ICommand<Unit>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.PatientsOwnerDelete;
    public string AggregateTypeName => "Owner";
    public string? AggregateIdOrNull => OwnerId;
}

public sealed class DeleteOwnerCommandValidator : IValidator<DeleteOwnerCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(DeleteOwnerCommand c, CancellationToken ct)
    {
        var failures = new List<ValidationFailure>();
        if (Rules.Required(c.OwnerId, nameof(c.OwnerId)) is { } id) failures.Add(id);
        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

public sealed class DeleteOwnerCommandHandler(IOwnerRepository owners, IPetRepository pets)
    : ICommandHandler<DeleteOwnerCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(DeleteOwnerCommand c, CommandContext context, CancellationToken ct)
    {
        var owner = await owners.FindAsync(c.OwnerId, ct);
        if (owner is null) return Result<Unit>.Fail(ErrorCodes.OwnerNotFound);

        foreach (var pet in await pets.ListByOwnerAsync(c.OwnerId, ct))
            pet.Deleted = true;

        owner.Deleted = true;
        return Result<Unit>.Ok(Unit.Value);
    }
}
