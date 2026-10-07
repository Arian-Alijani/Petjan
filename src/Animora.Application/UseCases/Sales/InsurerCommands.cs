using Animora.Application.Abstractions;
using Animora.Application.Auditing;
using Animora.Application.Commands;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.Validation;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Identity;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Sales;

namespace Animora.Application.UseCases.Sales;

internal static class InsurerRules
{
    public const int MaxNameLength = 128;

    public static IReadOnlyList<ValidationFailure> Validate(string name, decimal coveragePercent, long ceilingMinor)
    {
        var failures = new List<ValidationFailure>();
        if (Rules.Required(name, nameof(name)) is { } required) failures.Add(required);
        if (Rules.MaxLength(name, MaxNameLength, nameof(name)) is { } length) failures.Add(length);
        if (coveragePercent is < 0m or > 100m)
            failures.Add(new ValidationFailure(nameof(coveragePercent), "COVERAGE_PERCENT_RANGE"));
        if (ceilingMinor < 0) failures.Add(new ValidationFailure(nameof(ceilingMinor), "CEILING_NEGATIVE"));
        return failures;
    }
}

/// <summary>
/// Adds an insurer to the «پوشش بیمه‌ها» directory: a covered percentage plus an optional ceiling amount
/// (<c>0</c> = no ceiling). Returns the insurer id; it then appears in every invoice's insurance select.
/// </summary>
[RequiresPermission(PermissionCodes.SettingsWrite)]
[Auditable(AuditCategory.Configuration, CommandTypes.SalesInsurerCreate)]
public sealed record CreateInsurerCommand(string Name, decimal CoveragePercent, long CeilingMinor)
    : ICommand<string>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.SalesInsurerCreate;
    public string AggregateTypeName => "Insurer";
    public string? AggregateIdOrNull => null;
}

public sealed class CreateInsurerCommandValidator : IValidator<CreateInsurerCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(CreateInsurerCommand c, CancellationToken ct) =>
        ValueTask.FromResult(InsurerRules.Validate(c.Name, c.CoveragePercent, c.CeilingMinor));
}

public sealed class CreateInsurerCommandHandler(IInsurerRepository insurers)
    : ICommandHandler<CreateInsurerCommand, string>
{
    public async Task<Result<string>> HandleAsync(CreateInsurerCommand c, CommandContext context, CancellationToken ct)
    {
        var name = c.Name.Trim();
        if (await insurers.NameExistsAsync(name, null, ct))
            return Result<string>.Fail(ErrorCodes.InsurerNameDuplicate, $"Insurer '{name}' already exists.");

        var code = "INS-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var created = Insurer.Create(context.TenantId, code, name, c.CoveragePercent, c.CeilingMinor);
        insurers.Add(created);
        return Result<string>.Ok(created.Id);
    }
}

/// <summary>Changes an insurer's name, covered percentage and ceiling (already-applied invoices keep their own percentage).</summary>
[RequiresPermission(PermissionCodes.SettingsWrite)]
[Auditable(AuditCategory.Configuration, CommandTypes.SalesInsurerUpdate)]
public sealed record UpdateInsurerCommand(string Id, string Name, decimal CoveragePercent, long CeilingMinor)
    : ICommand<Unit>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.SalesInsurerUpdate;
    public string AggregateTypeName => "Insurer";
    public string? AggregateIdOrNull => Id;
}

public sealed class UpdateInsurerCommandValidator : IValidator<UpdateInsurerCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(UpdateInsurerCommand c, CancellationToken ct)
    {
        var failures = new List<ValidationFailure>(InsurerRules.Validate(c.Name, c.CoveragePercent, c.CeilingMinor));
        if (Rules.Required(c.Id, nameof(c.Id)) is { } id) failures.Add(id);
        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

public sealed class UpdateInsurerCommandHandler(IInsurerRepository insurers)
    : ICommandHandler<UpdateInsurerCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(UpdateInsurerCommand c, CommandContext context, CancellationToken ct)
    {
        var insurer = await insurers.FindAsync(c.Id, ct);
        if (insurer is null) return Result<Unit>.Fail(ErrorCodes.InsurerNotFound);

        var name = c.Name.Trim();
        if (await insurers.NameExistsAsync(name, c.Id, ct))
            return Result<Unit>.Fail(ErrorCodes.InsurerNameDuplicate, $"Insurer '{name}' already exists.");

        insurer.Update(name, c.CoveragePercent, c.CeilingMinor);
        return Result<Unit>.Ok(Unit.Value);
    }
}

/// <summary>
/// Removes an insurer from the directory (soft delete). Invoices that already used it keep their copied
/// percentage, so posted history is unchanged; it simply stops being offered on new invoices.
/// </summary>
[RequiresPermission(PermissionCodes.SettingsWrite)]
[Auditable(AuditCategory.Configuration, CommandTypes.SalesInsurerDelete)]
public sealed record DeleteInsurerCommand(string Id) : ICommand<Unit>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.SalesInsurerDelete;
    public string AggregateTypeName => "Insurer";
    public string? AggregateIdOrNull => Id;
}

public sealed class DeleteInsurerCommandValidator : IValidator<DeleteInsurerCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(DeleteInsurerCommand c, CancellationToken ct) =>
        ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(
            Rules.Required(c.Id, nameof(c.Id)) is { } id ? [id] : []);
}

public sealed class DeleteInsurerCommandHandler(IInsurerRepository insurers)
    : ICommandHandler<DeleteInsurerCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(DeleteInsurerCommand c, CommandContext context, CancellationToken ct)
    {
        var insurer = await insurers.FindAsync(c.Id, ct);
        if (insurer is null) return Result<Unit>.Fail(ErrorCodes.InsurerNotFound);

        insurer.Deleted = true;
        return Result<Unit>.Ok(Unit.Value);
    }
}
