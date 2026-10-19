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

/// <summary>
/// Records one stored file against a pet (Task 10b). The bytes are already in the content-addressed local
/// file store (that write happens in the client service, which owns the store); this command journals the
/// <see cref="DocumentReference"/> metadata so the row replicates, shows in the pet file, and can be re-opened
/// by its content hash. Reuses the pet-edit permission (no catalog churn).
/// </summary>
[RequiresPermission(PermissionCodes.PatientsPetEdit)]
[Auditable(AuditCategory.Clinical, "patients.pet.document.add")]
public sealed record AddPetDocumentCommand(string PetId, string Title, DocumentKind Kind, string FileId,
    long SizeBytes, string ContentHashSha256) : ICommand<string>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.PatientsPetDocumentAdd;
    public string AggregateTypeName => "Pet";
    public string? AggregateIdOrNull => PetId;
}

public sealed class AddPetDocumentCommandValidator : IValidator<AddPetDocumentCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(AddPetDocumentCommand c, CancellationToken ct)
    {
        var failures = new List<ValidationFailure>();
        if (Rules.Required(c.PetId, nameof(c.PetId)) is { } id) failures.Add(id);
        if (Rules.Required(c.Title, nameof(c.Title)) is { } title) failures.Add(title);
        else if (Rules.MaxLength(c.Title, 200, nameof(c.Title)) is { } tooLong) failures.Add(tooLong);
        if (Rules.Required(c.FileId, nameof(c.FileId)) is { } file) failures.Add(file);
        if (Rules.Required(c.ContentHashSha256, nameof(c.ContentHashSha256)) is { } hash) failures.Add(hash);
        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

public sealed class AddPetDocumentCommandHandler(
    IPetRepository pets, IDocumentReferenceRepository documents, IClock clock)
    : ICommandHandler<AddPetDocumentCommand, string>
{
    public async Task<Result<string>> HandleAsync(AddPetDocumentCommand c, CommandContext context, CancellationToken ct)
    {
        var pet = await pets.FindAsync(c.PetId, ct);
        if (pet is null) return Result<string>.Fail(ErrorCodes.PetNotFound);

        var reference = DocumentReference.Attach(context.TenantId, c.Title, c.Kind, c.FileId, c.SizeBytes,
            c.ContentHashSha256, context.UserId, clock, petId: c.PetId);
        if (reference.IsFailure) return Result<string>.Fail(reference.Error, reference.Detail);

        documents.Add(reference.Value!);
        return Result<string>.Ok(reference.Value!.Id);
    }
}
