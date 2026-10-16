using Animora.Application.Abstractions;
using Animora.Application.Auditing;
using Animora.Application.Commands;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.Validation;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Clinical;
using Animora.Domain.Identity;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Time;

namespace Animora.Application.UseCases.Visits;

/// <summary>A file attached to a radiology study as it travels in the command (bytes live in the file store).</summary>
public sealed record RadiologyAttachmentInput(string FileHash, string FileName, string ContentType);

/// <summary>One measured value of a lab result sheet as it travels in the command.</summary>
public sealed record LabResultItemInput(string TypeId, string TypeName, string Value, string? Unit);

/// <summary>Field limits shared by the radiology / lab validators and handlers.</summary>
internal static class ClinicalRecordLimits
{
    public const int BodyPart = 100;
    public const int Text = 4000;
    public const int FileName = 260;
    public const int ContentType = 128;
    public const int HashLength = 64;
    public const int MaxAttachments = 20;
    public const int MaxLabItems = 100;
    public const int LabValue = 200;
    public const int LabUnit = 40;
    public const int LabTypeName = 120;

    /// <summary>The wire-safe, normalised id the client chose, or <see langword="null"/> to derive one deterministically.</summary>
    public static string ResolveId(string? clientId, CommandContext context, IClock clock) =>
        string.IsNullOrWhiteSpace(clientId)
            ? IdGenerator.ForOperation(context.OperationId, 0, clock.UtcNow)
            : clientId.Trim();

    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

[RequiresPermission(PermissionCodes.VisitsWrite)]
[Auditable(AuditCategory.Clinical, CommandTypes.VisitsRadiologyAdd)]
public sealed record AddRadiologyCommand(string VisitId, DateTimeOffset TakenUtc, string BodyPart, string? Result,
    IReadOnlyList<RadiologyAttachmentInput>? Attachments = null, string? EntryId = null)
    : ICommand<string>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.VisitsRadiologyAdd;
    public string AggregateTypeName => "Visit";
    public string? AggregateIdOrNull => VisitId;
}

public sealed class AddRadiologyCommandValidator : IValidator<AddRadiologyCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(AddRadiologyCommand c, CancellationToken ct)
    {
        var errors = new List<ValidationFailure>();
        if (Rules.Required(c.VisitId, nameof(c.VisitId)) is { } id) errors.Add(id);
        if (Rules.Required(c.BodyPart, nameof(c.BodyPart)) is { } body) errors.Add(body);
        if (Rules.MaxLength(c.BodyPart, ClinicalRecordLimits.BodyPart, nameof(c.BodyPart)) is { } bodyLen) errors.Add(bodyLen);
        if (Rules.MaxLength(c.Result, ClinicalRecordLimits.Text, nameof(c.Result)) is { } result) errors.Add(result);
        if (c.Attachments is { Count: > ClinicalRecordLimits.MaxAttachments })
            errors.Add(new ValidationFailure(nameof(c.Attachments), "TOO_MANY"));
        foreach (var file in c.Attachments ?? [])
        {
            if (file is null || file.FileHash?.Length != ClinicalRecordLimits.HashLength
                || string.IsNullOrWhiteSpace(file.FileName) || file.FileName.Length > ClinicalRecordLimits.FileName
                || string.IsNullOrWhiteSpace(file.ContentType) || file.ContentType.Length > ClinicalRecordLimits.ContentType)
            {
                errors.Add(new ValidationFailure(nameof(c.Attachments), "INVALID"));
                break;
            }
        }

        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(errors);
    }
}

public sealed class AddRadiologyCommandHandler(IVisitRepository visits, IClock clock)
    : ICommandHandler<AddRadiologyCommand, string>
{
    public async Task<Result<string>> HandleAsync(AddRadiologyCommand c, CommandContext context, CancellationToken ct)
    {
        var visit = await visits.FindAsync(c.VisitId, ct);
        if (visit is null) return Result<string>.Fail(ErrorCodes.VisitNotFound);
        var id = ClinicalRecordLimits.ResolveId(c.EntryId, context, clock);
        var files = (c.Attachments ?? [])
            .Select(f => new RadiologyAttachment(f.FileHash.Trim().ToLowerInvariant(), f.FileName.Trim(), f.ContentType.Trim()))
            .ToArray();
        var record = new RadiologyRecord(id, c.TakenUtc.ToUniversalTime(), c.BodyPart.Trim(),
            ClinicalRecordLimits.Clean(c.Result), files);
        var added = visit.AddRadiology(record);
        return added.IsFailure ? Result<string>.Fail(ErrorCodes.VisitNotEditable) : Result<string>.Ok(id);
    }
}

[RequiresPermission(PermissionCodes.VisitsWrite)]
[Auditable(AuditCategory.Clinical, CommandTypes.VisitsRadiologyRemove)]
public sealed record RemoveRadiologyCommand(string VisitId, string RadiologyId) : ICommand<Unit>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.VisitsRadiologyRemove;
    public string AggregateTypeName => "Visit";
    public string? AggregateIdOrNull => VisitId;
}

public sealed class RemoveRadiologyCommandValidator : IValidator<RemoveRadiologyCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(RemoveRadiologyCommand c, CancellationToken ct)
    {
        var errors = new List<ValidationFailure>();
        if (Rules.Required(c.VisitId, nameof(c.VisitId)) is { } id) errors.Add(id);
        if (Rules.Required(c.RadiologyId, nameof(c.RadiologyId)) is { } record) errors.Add(record);
        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(errors);
    }
}

public sealed class RemoveRadiologyCommandHandler(IVisitRepository visits)
    : ICommandHandler<RemoveRadiologyCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(RemoveRadiologyCommand c, CommandContext context, CancellationToken ct)
    {
        var visit = await visits.FindAsync(c.VisitId, ct);
        if (visit is null) return Result<Unit>.Fail(ErrorCodes.VisitNotFound);
        if (visit.State is not (VisitState.Draft or VisitState.InProgress))
            return Result<Unit>.Fail(ErrorCodes.VisitNotEditable);
        var removed = visit.RemoveRadiology(c.RadiologyId);
        return removed.IsFailure ? Result<Unit>.Fail(ErrorCodes.RadiologyNotFound) : Result<Unit>.Ok(Unit.Value);
    }
}

[RequiresPermission(PermissionCodes.VisitsWrite)]
[Auditable(AuditCategory.Clinical, CommandTypes.VisitsLabAdd)]
public sealed record AddLabResultCommand(string VisitId, DateTimeOffset TakenUtc, string? Description,
    IReadOnlyList<LabResultItemInput>? Items = null, string? EntryId = null)
    : ICommand<string>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.VisitsLabAdd;
    public string AggregateTypeName => "Visit";
    public string? AggregateIdOrNull => VisitId;
}

public sealed class AddLabResultCommandValidator : IValidator<AddLabResultCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(AddLabResultCommand c, CancellationToken ct)
    {
        var errors = new List<ValidationFailure>();
        if (Rules.Required(c.VisitId, nameof(c.VisitId)) is { } id) errors.Add(id);
        if (Rules.MaxLength(c.Description, ClinicalRecordLimits.Text, nameof(c.Description)) is { } description) errors.Add(description);
        var items = c.Items ?? [];
        if (items.Count > ClinicalRecordLimits.MaxLabItems)
            errors.Add(new ValidationFailure(nameof(c.Items), "TOO_MANY"));
        // A sheet with neither notes nor a single measured value carries no information.
        if (items.Count == 0 && string.IsNullOrWhiteSpace(c.Description))
            errors.Add(new ValidationFailure(nameof(c.Items), "REQUIRED"));
        foreach (var item in items)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.TypeId) || string.IsNullOrWhiteSpace(item.TypeName)
                || item.TypeName.Length > ClinicalRecordLimits.LabTypeName
                || string.IsNullOrWhiteSpace(item.Value) || item.Value.Length > ClinicalRecordLimits.LabValue
                || item.Unit?.Length > ClinicalRecordLimits.LabUnit)
            {
                errors.Add(new ValidationFailure(nameof(c.Items), "INVALID"));
                break;
            }
        }

        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(errors);
    }
}

public sealed class AddLabResultCommandHandler(IVisitRepository visits, IClock clock)
    : ICommandHandler<AddLabResultCommand, string>
{
    public async Task<Result<string>> HandleAsync(AddLabResultCommand c, CommandContext context, CancellationToken ct)
    {
        var visit = await visits.FindAsync(c.VisitId, ct);
        if (visit is null) return Result<string>.Fail(ErrorCodes.VisitNotFound);
        var id = ClinicalRecordLimits.ResolveId(c.EntryId, context, clock);
        var items = (c.Items ?? [])
            .Select(i => new LabResultItem(i.TypeId.Trim(), i.TypeName.Trim(), i.Value.Trim(), ClinicalRecordLimits.Clean(i.Unit)))
            .ToArray();
        var result = new LabResult(id, c.TakenUtc.ToUniversalTime(), ClinicalRecordLimits.Clean(c.Description), items);
        var added = visit.AddLabResult(result);
        return added.IsFailure ? Result<string>.Fail(ErrorCodes.VisitNotEditable) : Result<string>.Ok(id);
    }
}

[RequiresPermission(PermissionCodes.VisitsWrite)]
[Auditable(AuditCategory.Clinical, CommandTypes.VisitsLabRemove)]
public sealed record RemoveLabResultCommand(string VisitId, string LabResultId) : ICommand<Unit>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.VisitsLabRemove;
    public string AggregateTypeName => "Visit";
    public string? AggregateIdOrNull => VisitId;
}

public sealed class RemoveLabResultCommandValidator : IValidator<RemoveLabResultCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(RemoveLabResultCommand c, CancellationToken ct)
    {
        var errors = new List<ValidationFailure>();
        if (Rules.Required(c.VisitId, nameof(c.VisitId)) is { } id) errors.Add(id);
        if (Rules.Required(c.LabResultId, nameof(c.LabResultId)) is { } record) errors.Add(record);
        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(errors);
    }
}

public sealed class RemoveLabResultCommandHandler(IVisitRepository visits)
    : ICommandHandler<RemoveLabResultCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(RemoveLabResultCommand c, CommandContext context, CancellationToken ct)
    {
        var visit = await visits.FindAsync(c.VisitId, ct);
        if (visit is null) return Result<Unit>.Fail(ErrorCodes.VisitNotFound);
        if (visit.State is not (VisitState.Draft or VisitState.InProgress))
            return Result<Unit>.Fail(ErrorCodes.VisitNotEditable);
        var removed = visit.RemoveLabResult(c.LabResultId);
        return removed.IsFailure ? Result<Unit>.Fail(ErrorCodes.LabResultNotFound) : Result<Unit>.Ok(Unit.Value);
    }
}
