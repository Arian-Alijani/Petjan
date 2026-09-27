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
using Animora.Domain.Time;

namespace Animora.Application.UseCases.Patients;

/// <summary>Appends a weight reading to a pet's growth history.</summary>
[RequiresPermission(PermissionCodes.PatientsPetEdit)]
[Auditable(AuditCategory.Clinical, "patients.pet.weight.add")]
public sealed record AddWeightEntryCommand(string PetId, decimal WeightKg, DateTimeOffset MeasuredUtc, string? Note)
    : ICommand<Unit>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.PatientsPetWeightAdd;
    public string AggregateTypeName => "Pet";
    public string? AggregateIdOrNull => PetId;
}

public sealed class AddWeightEntryCommandValidator : IValidator<AddWeightEntryCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(AddWeightEntryCommand c, CancellationToken ct)
    {
        IReadOnlyList<ValidationFailure> failures =
            Rules.Required(c.PetId, nameof(c.PetId)) is { } id ? [id] : [];
        return ValueTask.FromResult(failures);
    }
}

public sealed class AddWeightEntryCommandHandler(IPetRepository pets, IWeightEntryRepository weights, IClock clock)
    : ICommandHandler<AddWeightEntryCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(AddWeightEntryCommand c, CommandContext context, CancellationToken ct)
    {
        if (c.WeightKg <= 0m) return Result<Unit>.Fail(ErrorCodes.WeightEntryInvalid, "Weight must be positive.");
        var pet = await pets.FindAsync(c.PetId, ct);
        if (pet is null) return Result<Unit>.Fail(ErrorCodes.PetNotFound);
        var entry = WeightEntry.Record(context.TenantId, c.PetId, c.WeightKg, c.MeasuredUtc, context.UserId, clock, c.Note);
        if (entry.IsFailure) return Result<Unit>.Fail(ErrorCodes.WeightEntryInvalid, entry.Detail);
        weights.Add(entry.Value!);
        return Result<Unit>.Ok(Unit.Value);
    }
}
