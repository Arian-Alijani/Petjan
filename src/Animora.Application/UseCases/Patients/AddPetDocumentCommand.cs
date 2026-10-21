using Animora.Application.Abstractions;
using Animora.Application.Auditing;
using Animora.Application.Commands;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.Validation;
using Animora.Contracts.Patients;
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
/// <para>
/// <c>FileId</c> is the server file object's id (the payload is uploaded through <c>/files</c> before this command
/// is dispatched, so the bytes exist server-side and can be re-fetched on a rebuilt store); <c>ContentHashSha256</c>
/// is the SHA-256 of the same bytes and stays the local content-addressed key.
/// </para>
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
        // The byte count is the one property of the payload this layer can see, so it is bounded here too: a
        // client that skipped its own pre-flight still cannot record an empty or oversized attachment.
        if (Rules.Range(c.SizeBytes, 1L, DocumentFilePolicy.MaxBytes, nameof(c.SizeBytes)) is { } size)
            failures.Add(size);
        else if (c.ContentHashSha256 is { Length: 64 } sha && !sha.All(Uri.IsHexDigit))
            failures.Add(new ValidationFailure(nameof(c.ContentHashSha256), ErrorCodes.FileHashMismatch.Code));
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
