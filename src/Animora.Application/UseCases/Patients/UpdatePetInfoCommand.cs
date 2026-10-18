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

/// <summary>Updates a pet's descriptive profile (NOT species — that is immutable).</summary>
[RequiresPermission(PermissionCodes.PatientsPetEdit)]
[Auditable(AuditCategory.Clinical, "patients.pet.update")]
public sealed record UpdatePetInfoCommand(string PetId, string Name, string? Breed, string? Color,
    PetGender Gender, DateTimeOffset? BirthUtc, bool Neutered, string? AllergyNotes, string? ChronicNotes,
    string? Habitat = null, string? Behavior = null, string? Diet = null, string? BloodType = null,
    string? Insurance = null, string? MicrochipCode = null, DateTimeOffset? MicrochipUtc = null,
    string? MicrochipVendor = null, string? MicrochipLocation = null, DateTimeOffset? NeuteredUtc = null)
    : ICommand<Unit>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.PatientsPetUpdate;
    public string AggregateTypeName => "Pet";
    public string? AggregateIdOrNull => PetId;
}

public sealed class UpdatePetInfoCommandValidator : IValidator<UpdatePetInfoCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(UpdatePetInfoCommand c, CancellationToken ct)
    {
        var failures = new List<ValidationFailure>();
        if (Rules.Required(c.PetId, nameof(c.PetId)) is { } id) failures.Add(id);
        if (Rules.Required(c.Name, nameof(c.Name)) is { } name) failures.Add(name);
        else if (Rules.MaxLength(c.Name, 120, nameof(c.Name)) is { } tooLong) failures.Add(tooLong);
        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

public sealed class UpdatePetInfoCommandHandler(IPetRepository pets) : ICommandHandler<UpdatePetInfoCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(UpdatePetInfoCommand c, CommandContext context, CancellationToken ct)
    {
        var pet = await pets.FindAsync(c.PetId, ct);
        if (pet is null) return Result<Unit>.Fail(ErrorCodes.PetNotFound);
        pet.UpdateDetails(c.Name, c.Breed, c.Gender, c.BirthUtc, c.Neutered, c.Color,
            c.Habitat, c.Behavior, c.Diet, c.BloodType, c.Insurance, c.NeuteredUtc);
        pet.SetMicrochip(c.MicrochipCode, c.MicrochipUtc, c.MicrochipVendor, c.MicrochipLocation);
        pet.SetClinicalFlags(c.AllergyNotes, c.ChronicNotes);
        return Result<Unit>.Ok(Unit.Value);
    }
}
