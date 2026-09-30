using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.Text;
using Animora.Application.UseCases.Inventory.ReadModels;
using Animora.Domain.Inventory;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Converters;
using Animora.Persistence.Sqlite.Entities;
using Animora.Persistence.Sqlite.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Animora.Persistence.Sqlite.Queries;

/// <summary>
/// Snapshot-joined inventory reads (C06). On-hand is taken from the maintained
/// <see cref="StockOnHandSnapshot"/> cache (O(1) per key), never a live SUM over the ledger. Tenant
/// and soft-delete filters are applied automatically by <c>ModelConventions</c>.
/// </summary>
public sealed class InventoryQueries(AnimoraLocalDbContext db, IOptions<LocalStoreOptions> options)
    : IInventoryQueries
{
    private const int MaxCatalogScan = 1000; // Bounded scan before in-memory low-stock paging.
    private const int MaxMovementHistory = 100;

    private LocalStoreOptions Options => options.Value;

    public async Task<PagedResult<ProductReadModel>> GetCatalogAsync(
        string? search, string? categoryId, bool lowStockOnly, int page, int pageSize, CancellationToken ct)
    {
        var q = db.Set<Product>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            q = q.Where(p => EF.Functions.Like(p.Name, $"%{term}%") || EF.Functions.Like(p.Sku, $"%{term}%"));
        }
        if (!string.IsNullOrWhiteSpace(categoryId))
        {
            q = q.Where(p => p.CategoryId == categoryId);
        }

        // Bounded, stably-ordered scan; variants auto-include with the aggregate.
        var products = await q.OrderBy(p => p.Name).ThenBy(p => p.Id).Take(MaxCatalogScan).ToListAsync(ct);
        if (products.Count == 0)
        {
            return new PagedResult<ProductReadModel>([], 0, page, pageSize);
        }

        var onHand = await LoadOnHandAsync(products.Select(p => p.Id), ct);
        var rows = new List<ProductReadModel>(products.Count);
        for (var i = 0; i < products.Count; i++)
        {
            var row = ToReadModel(products[i], onHand);
            if (lowStockOnly && !IsLowStock(row))
            {
                continue;
            }
            rows.Add(row);
        }

        var max = Math.Clamp(Options.MaxPageSize, 1, QueryPaging.HardMaxPageSize);
        var size = Math.Clamp(pageSize, 1, max);
        var pageIndex = Math.Max(1, page);
        var skip = (pageIndex - 1) * size;
        IReadOnlyList<ProductReadModel> items = skip >= rows.Count
            ? []
            : rows.GetRange(skip, Math.Min(size, rows.Count - skip));
        return new PagedResult<ProductReadModel>(items, rows.Count, pageIndex, size);
    }

    public async Task<ProductDetailReadModel?> GetDetailAsync(string productId, CancellationToken ct)
    {
        var product = await db.Set<Product>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId, ct);
        if (product is null)
        {
            return null;
        }

        var onHand = await LoadOnHandAsync([product.Id], ct);
        var rows = await db.Set<StockMovement>().AsNoTracking()
            .Where(m => m.ProductId == productId)
            .OrderByDescending(m => m.OccurredUtc).ThenByDescending(m => m.Id)
            .Take(MaxMovementHistory)
            .ToListAsync(ct);
        var movements = new List<MovementReadModel>(rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            movements.Add(ToMovement(rows[i]));
        }
        return new ProductDetailReadModel(ToReadModel(product, onHand), movements);
    }

    public async Task<IReadOnlyList<CategoryReadModel>> GetCategoriesAsync(CancellationToken ct) =>
        await db.Set<ProductCategory>().AsNoTracking().Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Code)
            .Select(c => new CategoryReadModel(c.Id, c.DisplayName, c.ParentCategoryId ?? string.Empty))
            .ToListAsync(ct);

    public async Task<PagedResult<MovementReadModel>> GetMovementHistoryAsync(
        string productId, int page, int pageSize, CancellationToken ct)
    {
        var q = db.Set<StockMovement>().AsNoTracking()
            .Where(m => m.ProductId == productId)
            .OrderByDescending(m => m.OccurredUtc).ThenByDescending(m => m.Id);
        var paged = await q.ToPagedAsync(page, pageSize, Options, ct);
        var items = new List<MovementReadModel>(paged.Items.Count);
        for (var i = 0; i < paged.Items.Count; i++)
        {
            items.Add(ToMovement(paged.Items[i]));
        }
        return new PagedResult<MovementReadModel>(items, paged.TotalCount, paged.Page, paged.PageSize);
    }

    public async Task<long> GetOnHandScaledAsync(string productId, string variantId, CancellationToken ct)
    {
        var row = await db.Set<StockOnHandSnapshot>().AsNoTracking()
            .Where(s => s.ProductId == productId && s.VariantId == variantId)
            .Select(s => (long?)s.OnHandScaled)
            .FirstOrDefaultAsync(ct);
        return row ?? 0L;
    }

    public async Task<long> GetProductOnHandScaledAsync(string productId, CancellationToken ct)
    {
        var rows = await db.Set<StockOnHandSnapshot>().AsNoTracking()
            .Where(s => s.ProductId == productId)
            .Select(s => s.OnHandScaled)
            .ToListAsync(ct);
        var total = 0L;
        for (var i = 0; i < rows.Count; i++)
        {
            total += rows[i];
        }
        return total;
    }

    public Task<int> CountMovementsSinceAsync(string productId, DateTimeOffset sinceUtc, CancellationToken ct) =>
        db.Set<StockMovement>().AsNoTracking()
            .CountAsync(m => m.ProductId == productId && m.OccurredUtc >= sinceUtc, ct);

    private async Task<Dictionary<(string ProductId, string VariantId), long>> LoadOnHandAsync(
        IEnumerable<string> productIds, CancellationToken ct)
    {
        var ids = productIds.Distinct(StringComparer.Ordinal).ToArray();
        var rows = await db.Set<StockOnHandSnapshot>().AsNoTracking()
            .Where(s => ids.Contains(s.ProductId))
            .Select(s => new { s.ProductId, s.VariantId, s.OnHandScaled })
            .ToListAsync(ct);
        var map = new Dictionary<(string, string), long>(rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            map[(rows[i].ProductId, rows[i].VariantId)] = rows[i].OnHandScaled;
        }
        return map;
    }

    private static ProductReadModel ToReadModel(
        Product product, IReadOnlyDictionary<(string, string), long> onHand)
    {
        var variants = new List<ProductVariantReadModel>(product.Variants.Count);
        for (var i = 0; i < product.Variants.Count; i++)
        {
            var v = product.Variants[i];
            var scaled = onHand.TryGetValue((product.Id, v.VariantId), out var s) ? s : 0L;
            variants.Add(new ProductVariantReadModel(
                v.VariantId, v.Sku, v.AttributeName, v.AttributeValue,
                v.CostPrice.Minor, v.SalePrice.Minor,
                FixedScaleQuantityConverter.ToScaled(v.ReorderLevel.Value),
                v.Barcode, v.IsActive, scaled));
        }
        return new ProductReadModel(
            product.Id, product.Sku, product.Name, product.Brand ?? string.Empty,
            product.CategoryId, product.Kind.ToString(), product.UnitCode, product.Notes,
            product.IsActive, variants);
    }

    private static MovementReadModel ToMovement(StockMovement m) => new(
        m.Id, m.ProductId, m.VariantId, m.Kind.ToString(),
        FixedScaleQuantityConverter.ToScaled(m.Quantity.Value), m.UnitCost.Minor,
        m.OccurredUtc, m.RecordedByUserId, m.SourceEntityType, m.SourceEntityId, m.Reason);

    private static bool IsLowStock(ProductReadModel product)
    {
        for (var i = 0; i < product.Variants.Count; i++)
        {
            var v = product.Variants[i];
            if (v.IsActive && v.OnHandScaled <= v.ReorderLevelScaled)
            {
                return true;
            }
        }
        return false;
    }
}
