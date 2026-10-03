using Animora.Application.Abstractions;
using Animora.Application.UseCases.Inventory.ReadModels;

namespace Animora.Application.Ports;

/// <summary>
/// Snapshot-joined inventory read projections (C06). The catalog and detail reads take on-hand from
/// the maintained <c>StockOnHandSnapshot</c> cache (O(1)), never a live SUM over the ledger.
/// </summary>
public interface IInventoryQueries
{
    /// <summary>Paged catalog; <paramref name="lowStockOnly"/> keeps only variants at/below reorder.</summary>
    Task<PagedResult<ProductReadModel>> GetCatalogAsync(
        string? search, string? categoryId, bool lowStockOnly, int page, int pageSize, CancellationToken ct);

    /// <summary>One product with its variants (snapshot on-hand) and its last 100 movements.</summary>
    Task<ProductDetailReadModel?> GetDetailAsync(string productId, CancellationToken ct);

    /// <summary>The product categories.</summary>
    Task<IReadOnlyList<CategoryReadModel>> GetCategoriesAsync(CancellationToken ct);

    /// <summary>A product's movement history, newest first, paged.</summary>
    Task<PagedResult<MovementReadModel>> GetMovementHistoryAsync(
        string productId, int page, int pageSize, CancellationToken ct);

    /// <summary>Snapshot on-hand (scaled ×1000) for one variant; 0 when it has no movements.</summary>
    Task<long> GetOnHandScaledAsync(string productId, string variantId, CancellationToken ct);

    /// <summary>Total scaled on-hand across every variant of a product; 0 when none.</summary>
    Task<long> GetProductOnHandScaledAsync(string productId, CancellationToken ct);

    /// <summary>Whether another product (not <paramref name="excludeProductId"/>) already uses <paramref name="sku"/>.</summary>
    Task<bool> ProductSkuExistsAsync(string sku, string excludeProductId, CancellationToken ct);

    /// <summary>How many movements the product has had since <paramref name="sinceUtc"/>.</summary>
    Task<int> CountMovementsSinceAsync(string productId, DateTimeOffset sinceUtc, CancellationToken ct);
}
