using Animora.Application.Abstractions;
using Animora.Application.Dispatch;
using Animora.Application.UseCases.Inventory.ReadModels;

namespace Animora.Application.UseCases.Inventory;

/// <summary>
/// Registers the C06 inventory use cases: the four catalogue queries and the five catalogue/movement
/// commands. Chained into <c>AddAnimoraApplication</c> from the composition root (C06 T11).
/// </summary>
public static class InventoryRegistration
{
    public static ApplicationRegistrationBuilder AddInventoryUseCases(this ApplicationRegistrationBuilder builder) => builder
        .AddQuery<ProductCatalogQuery, PagedResult<ProductReadModel>, ProductCatalogQueryHandler>()
        .AddQuery<ProductDetailQuery, ProductDetailReadModel?, ProductDetailQueryHandler>()
        .AddQuery<ProductCategoriesQuery, IReadOnlyList<CategoryReadModel>, ProductCategoriesQueryHandler>()
        .AddQuery<StockMovementHistoryQuery, PagedResult<MovementReadModel>, StockMovementHistoryQueryHandler>()
        .AddCommand<CreateProductCommand, string, CreateProductCommandHandler>()
        .AddValidator<CreateProductCommand, CreateProductCommandValidator>()
        .AddCommand<UpdateProductInfoCommand, Unit, UpdateProductInfoCommandHandler>()
        .AddValidator<UpdateProductInfoCommand, UpdateProductInfoCommandValidator>()
        .AddCommand<AddProductVariantCommand, string, AddProductVariantCommandHandler>()
        .AddValidator<AddProductVariantCommand, AddProductVariantCommandValidator>()
        .AddCommand<DeleteProductCommand, Unit, DeleteProductCommandHandler>()
        .AddValidator<DeleteProductCommand, DeleteProductCommandValidator>()
        .AddCommand<AdjustStockCommand, Unit, AdjustStockCommandHandler>()
        .AddValidator<AdjustStockCommand, AdjustStockCommandValidator>();
}
