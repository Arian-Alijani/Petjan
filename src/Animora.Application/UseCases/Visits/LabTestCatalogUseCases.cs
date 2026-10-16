using Animora.Application.Abstractions;
using Animora.Application.Commands;
using Animora.Application.Dispatch;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.Validation;
using Animora.Contracts.Visits;
using Animora.Domain.Identity;
using Animora.Domain.Results;

namespace Animora.Application.UseCases.Visits;

// Lab-test types are clinic-wide configuration (preferences), exactly like the appointment settings: reads need
// visit visibility (the lab form cannot render its type picker without them); every write needs SettingsWrite,
// enforced by the dispatcher's authorization stage. Not journaled (not replicated business intent) and
// self-transacted because no database row is written.

[RequiresPermission(PermissionCodes.VisitsRead)]
public sealed record GetLabTestTypesQuery : IQuery<IReadOnlyList<LabTestType>>;

public sealed class GetLabTestTypesQueryHandler(ILabTestCatalogStore store)
    : IQueryHandler<GetLabTestTypesQuery, IReadOnlyList<LabTestType>>
{
    public async Task<Result<IReadOnlyList<LabTestType>>> HandleAsync(GetLabTestTypesQuery q, QueryContext context,
        CancellationToken ct) => Result<IReadOnlyList<LabTestType>>.Ok(await store.GetAsync(ct).ConfigureAwait(false));
}

[RequiresPermission(PermissionCodes.SettingsWrite)]
public sealed record AddLabTestTypeCommand(string Name, string? DefaultUnit) : ICommand<LabTestType>, ISelfTransactedCommand;

public sealed class AddLabTestTypeCommandValidator : IValidator<AddLabTestTypeCommand>
{
    public const int MaxNameLength = 60;
    public const int MaxUnitLength = 20;

    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(AddLabTestTypeCommand c, CancellationToken ct)
    {
        var errors = new List<ValidationFailure>();
        if (Rules.Required(c.Name, nameof(c.Name)) is { } name) errors.Add(name);
        if (Rules.MaxLength(c.Name, MaxNameLength, nameof(c.Name)) is { } nameLength) errors.Add(nameLength);
        if (Rules.MaxLength(c.DefaultUnit, MaxUnitLength, nameof(c.DefaultUnit)) is { } unit) errors.Add(unit);
        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(errors);
    }
}

public sealed class AddLabTestTypeCommandHandler(ILabTestCatalogStore store)
    : ICommandHandler<AddLabTestTypeCommand, LabTestType>
{
    public async Task<Result<LabTestType>> HandleAsync(AddLabTestTypeCommand c, CommandContext context, CancellationToken ct)
    {
        var added = await store.AddTypeAsync(c.Name, c.DefaultUnit, ct).ConfigureAwait(false);
        return added is null
            ? Result<LabTestType>.Fail(ErrorCodes.LabTestTypeDuplicate)
            : Result<LabTestType>.Ok(added);
    }
}

[RequiresPermission(PermissionCodes.SettingsWrite)]
public sealed record RemoveLabTestTypeCommand(string TypeId) : ICommand<Unit>, ISelfTransactedCommand;

public sealed class RemoveLabTestTypeCommandValidator : IValidator<RemoveLabTestTypeCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(RemoveLabTestTypeCommand c, CancellationToken ct) =>
        ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(
            Rules.Required(c.TypeId, nameof(c.TypeId)) is { } error ? [error] : []);
}

public sealed class RemoveLabTestTypeCommandHandler(ILabTestCatalogStore store)
    : ICommandHandler<RemoveLabTestTypeCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(RemoveLabTestTypeCommand c, CommandContext context, CancellationToken ct)
    {
        await store.RemoveTypeAsync(c.TypeId, ct).ConfigureAwait(false);
        return Result<Unit>.Ok(Unit.Value);
    }
}

/// <summary>Registers the lab-test catalog use cases. Desktop-only: the catalog is a local preference, not a wire command.</summary>
public static class LabTestCatalogRegistration
{
    public static ApplicationRegistrationBuilder AddLabTestCatalogUseCases(this ApplicationRegistrationBuilder builder) => builder
        .AddQuery<GetLabTestTypesQuery, IReadOnlyList<LabTestType>, GetLabTestTypesQueryHandler>()
        .AddCommand<AddLabTestTypeCommand, LabTestType, AddLabTestTypeCommandHandler>()
        .AddValidator<AddLabTestTypeCommand, AddLabTestTypeCommandValidator>()
        .AddCommand<RemoveLabTestTypeCommand, Unit, RemoveLabTestTypeCommandHandler>()
        .AddValidator<RemoveLabTestTypeCommand, RemoveLabTestTypeCommandValidator>();
}
