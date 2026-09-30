using Animora.Application.Abstractions;
using Animora.Application.Auditing;
using Animora.Application.Commands;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Domain.Identity;
using Animora.Domain.Results;
namespace Animora.Application.UseCases.Inventory;

[RequiresPermission(PermissionCodes.InventoryAdjust)]
[Auditable(AuditCategory.Inventory, "inventory.category.delete")]
public sealed record DeleteProductCategoryCommand(string CategoryId) : ICommand<Unit>, IJournaledCommand
{
    public string CommandTypeName => "inventory.category.delete";
    public string AggregateTypeName => "ProductCategory";
    public string? AggregateIdOrNull => CategoryId;
}

public sealed class DeleteProductCategoryCommandHandler(IProductCategoryStore categories)
    : ICommandHandler<DeleteProductCategoryCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(DeleteProductCategoryCommand command, CommandContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.CategoryId)) return Result<Unit>.Fail(ErrorCodes.ValidationFailed);
        var category = await categories.FindAsync(command.CategoryId, ct);
        if (category is null) return Result<Unit>.Fail(ErrorCodes.NotFound);
        // The dispatcher UoW keeps blocker checks and tombstone in the same transaction.
        if (await categories.HasProductsAsync(category.Id, ct) || await categories.HasChildrenAsync(category.Id, ct))
            return Result<Unit>.Fail(ErrorCodes.CategoryInUse);
        category.Deleted = true;
        return Result<Unit>.Ok(Unit.Value);
    }
}
