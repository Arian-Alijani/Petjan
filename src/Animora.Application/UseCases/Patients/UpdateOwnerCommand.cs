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

/// <summary>Updates an existing owner's contact/demographic block.</summary>
[RequiresPermission(PermissionCodes.PatientsOwnerEdit)]
[Auditable(AuditCategory.Clinical, "patients.owner.update")]
public sealed record UpdateOwnerCommand(string OwnerId, string FullName, string PhonePrimary,
    OwnerGender Gender, string? PhoneSecondary, string? NationalId, string? Address)
    : ICommand<Unit>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.PatientsOwnerUpdate;
    public string AggregateTypeName => "Owner";
    public string? AggregateIdOrNull => OwnerId;
}

public sealed class UpdateOwnerCommandValidator : IValidator<UpdateOwnerCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(UpdateOwnerCommand c, CancellationToken ct)
    {
        var failures = new List<ValidationFailure>();
        if (Rules.Required(c.OwnerId, nameof(c.OwnerId)) is { } id) failures.Add(id);
        if (Rules.Required(c.FullName, nameof(c.FullName)) is { } name) failures.Add(name);
        else if (Rules.MaxLength(c.FullName, 120, nameof(c.FullName)) is { } tooLong) failures.Add(tooLong);
        if (Rules.Required(c.PhonePrimary, nameof(c.PhonePrimary)) is { } phone) failures.Add(phone);
        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

public sealed class UpdateOwnerCommandHandler(IOwnerRepository owners) : ICommandHandler<UpdateOwnerCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(UpdateOwnerCommand c, CommandContext context, CancellationToken ct)
    {
        var owner = await owners.FindAsync(c.OwnerId, ct);
        if (owner is null) return Result<Unit>.Fail(ErrorCodes.OwnerNotFound);
        owner.UpdateContactDetails(c.FullName, c.PhonePrimary, c.PhoneSecondary, c.NationalId,
            email: null, address: c.Address, city: null, gender: c.Gender, notes: null);
        return Result<Unit>.Ok(Unit.Value);   // TransactionBehavior commits the tracked change.
    }
}
