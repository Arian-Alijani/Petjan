using Animora.Application.Abstractions;
using Animora.Application.Auditing;
using Animora.Application.Commands;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.Validation;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Identity;
using Animora.Domain.Inventory;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Time;

namespace Animora.Application.UseCases.Inventory;

/// <summary>Creates a root product category with a unique display name. Returns the new category id.</summary>
[RequiresPermission(PermissionCodes.InventoryAdjust)]
[Auditable(AuditCategory.Inventory, CommandTypes.InventoryCategoryCreate)]
public sealed record CreateProductCategoryCommand(string Name) : ICommand<string>, IJournaledCommand
{
    public const int MaxNameLength = 64;
    public string CommandTypeName => CommandTypes.InventoryCategoryCreate;
    public string AggregateTypeName => "ProductCategory";
    public string? AggregateIdOrNull => null;
}

public sealed class CreateProductCategoryCommandValidator : IValidator<CreateProductCategoryCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(CreateProductCategoryCommand c, CancellationToken ct)
    {
        var failures = new List<ValidationFailure>();
        if (Rules.Required(c.Name, nameof(c.Name)) is { } name) failures.Add(name);
        if (Rules.MaxLength(c.Name, CreateProductCategoryCommand.MaxNameLength, nameof(c.Name)) is { } length) failures.Add(length);
        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

public sealed class CreateProductCategoryCommandHandler(IProductCategoryRepository categories, IClock clock)
    : ICommandHandler<CreateProductCategoryCommand, string>
{
    public async Task<Result<string>> HandleAsync(CreateProductCategoryCommand c, CommandContext context, CancellationToken ct)
    {
        var name = c.Name.Trim();
        if (await categories.NameExistsAsync(name, null, ct))
            return Result<string>.Fail(ErrorCodes.CategoryNameDuplicate, $"Category '{name}' already exists.");

        var code = "CAT-" + IdGenerator.CodeSuffix(context.OperationId, 8);
        var created = ProductCategory.Create(context.TenantId, code, name, sortOrder: await categories.NextSortOrderAsync(ct),
            id: IdGenerator.ForOperation(context.OperationId, 0, clock.UtcNow));
        if (created.IsFailure) return Result<string>.Fail(created.Error, created.Detail);

        categories.Add(created.Value!);
        return Result<string>.Ok(created.Value!.Id);
    }
}

/// <summary>Renames a category (its id and products are unchanged).</summary>
[RequiresPermission(PermissionCodes.InventoryAdjust)]
[Auditable(AuditCategory.Inventory, CommandTypes.InventoryCategoryRename)]
public sealed record RenameProductCategoryCommand(string CategoryId, string Name) : ICommand<Unit>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.InventoryCategoryRename;
    public string AggregateTypeName => "ProductCategory";
    public string? AggregateIdOrNull => CategoryId;
}

public sealed class RenameProductCategoryCommandValidator : IValidator<RenameProductCategoryCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(RenameProductCategoryCommand c, CancellationToken ct)
    {
        var failures = new List<ValidationFailure>();
        if (Rules.Required(c.CategoryId, nameof(c.CategoryId)) is { } id) failures.Add(id);
        if (Rules.Required(c.Name, nameof(c.Name)) is { } name) failures.Add(name);
        if (Rules.MaxLength(c.Name, CreateProductCategoryCommand.MaxNameLength, nameof(c.Name)) is { } length) failures.Add(length);
        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

public sealed class RenameProductCategoryCommandHandler(IProductCategoryRepository categories)
    : ICommandHandler<RenameProductCategoryCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(RenameProductCategoryCommand c, CommandContext context, CancellationToken ct)
    {
        var category = await categories.FindAsync(c.CategoryId, ct);
        if (category is null) return Result<Unit>.Fail(ErrorCodes.CategoryNotFound);

        var name = c.Name.Trim();
        if (await categories.NameExistsAsync(name, category.Id, ct))
            return Result<Unit>.Fail(ErrorCodes.CategoryNameDuplicate, $"Category '{name}' already exists.");

        var renamed = category.Rename(name, category.SortOrder);
        return renamed.IsFailure ? Result<Unit>.Fail(renamed.Error, renamed.Detail) : Result<Unit>.Ok(Unit.Value);
    }
}

/// <summary>Soft-deletes an empty category. Refused with <c>CATEGORY_IN_USE</c> while any product references it.</summary>
[RequiresPermission(PermissionCodes.InventoryAdjust)]
[Auditable(AuditCategory.Inventory, CommandTypes.InventoryCategoryDelete)]
public sealed record DeleteProductCategoryCommand(string CategoryId) : ICommand<Unit>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.InventoryCategoryDelete;
    public string AggregateTypeName => "ProductCategory";
    public string? AggregateIdOrNull => CategoryId;
}

public sealed class DeleteProductCategoryCommandValidator : IValidator<DeleteProductCategoryCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(DeleteProductCategoryCommand c, CancellationToken ct)
    {
        var failures = new List<ValidationFailure>();
        if (Rules.Required(c.CategoryId, nameof(c.CategoryId)) is { } id) failures.Add(id);
        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

public sealed class DeleteProductCategoryCommandHandler(IProductCategoryRepository categories)
    : ICommandHandler<DeleteProductCategoryCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(DeleteProductCategoryCommand c, CommandContext context, CancellationToken ct)
    {
        var category = await categories.FindAsync(c.CategoryId, ct);
        if (category is null) return Result<Unit>.Fail(ErrorCodes.CategoryNotFound);

        if (await categories.CountProductsAsync(category.Id, ct) > 0)
            return Result<Unit>.Fail(ErrorCodes.CategoryInUse, $"Category '{category.Id}' still has products.");

        var deactivated = category.Deactivate();
        if (deactivated.IsFailure) return Result<Unit>.Fail(deactivated.Error, deactivated.Detail);
        category.Deleted = true;
        return Result<Unit>.Ok(Unit.Value);
    }
}
