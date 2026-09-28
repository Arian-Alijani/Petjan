using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.UseCases.Inventory.ReadModels;
using Animora.Domain.Identity;
using Animora.Domain.Results;

namespace Animora.Application.UseCases.Inventory;

/// <summary>
/// The inventory catalog read (C06): search + category filter + low-stock filter, paged. On-hand
/// comes from the maintained snapshot cache, never a live ledger SUM (see <see cref="IInventoryQueries"/>).
/// </summary>
[RequiresPermission(PermissionCodes.InventoryRead)]
public sealed record ProductCatalogQuery(
    string? Search = null,
    string? CategoryId = null,
    bool LowStockOnly = false,
    int Page = 1,
    int PageSize = 50) : IQuery<PagedResult<ProductReadModel>>;

public sealed class ProductCatalogQueryHandler(IInventoryQueries queries)
    : IQueryHandler<ProductCatalogQuery, PagedResult<ProductReadModel>>
{
    public async Task<Result<PagedResult<ProductReadModel>>> HandleAsync(
        ProductCatalogQuery query, QueryContext context, CancellationToken ct) =>
        Result<PagedResult<ProductReadModel>>.Ok(await queries.GetCatalogAsync(
            query.Search, query.CategoryId, query.LowStockOnly, query.Page, query.PageSize, ct));
}

/// <summary>One product with its variants (snapshot on-hand) and its last 100 movements.</summary>
[RequiresPermission(PermissionCodes.InventoryRead)]
public sealed record ProductDetailQuery(string ProductId) : IQuery<ProductDetailReadModel?>;

public sealed class ProductDetailQueryHandler(IInventoryQueries queries)
    : IQueryHandler<ProductDetailQuery, ProductDetailReadModel?>
{
    public async Task<Result<ProductDetailReadModel?>> HandleAsync(
        ProductDetailQuery query, QueryContext context, CancellationToken ct) =>
        Result<ProductDetailReadModel?>.Ok(await queries.GetDetailAsync(query.ProductId, ct));
}

/// <summary>The product categories backing the filter chips and the category settings list.</summary>
[RequiresPermission(PermissionCodes.InventoryRead)]
public sealed record ProductCategoriesQuery : IQuery<IReadOnlyList<CategoryReadModel>>;

public sealed class ProductCategoriesQueryHandler(IInventoryQueries queries)
    : IQueryHandler<ProductCategoriesQuery, IReadOnlyList<CategoryReadModel>>
{
    public async Task<Result<IReadOnlyList<CategoryReadModel>>> HandleAsync(
        ProductCategoriesQuery query, QueryContext context, CancellationToken ct) =>
        Result<IReadOnlyList<CategoryReadModel>>.Ok(await queries.GetCategoriesAsync(ct));
}

/// <summary>A product's stock-movement ledger, newest first, paged.</summary>
[RequiresPermission(PermissionCodes.InventoryRead)]
public sealed record StockMovementHistoryQuery(string ProductId, int Page = 1, int PageSize = 50)
    : IQuery<PagedResult<MovementReadModel>>;

public sealed class StockMovementHistoryQueryHandler(IInventoryQueries queries)
    : IQueryHandler<StockMovementHistoryQuery, PagedResult<MovementReadModel>>
{
    public async Task<Result<PagedResult<MovementReadModel>>> HandleAsync(
        StockMovementHistoryQuery query, QueryContext context, CancellationToken ct) =>
        Result<PagedResult<MovementReadModel>>.Ok(await queries.GetMovementHistoryAsync(
            query.ProductId, query.Page, query.PageSize, ct));
}
