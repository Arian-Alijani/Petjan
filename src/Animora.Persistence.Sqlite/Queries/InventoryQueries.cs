using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.Text;
using Animora.Application.UseCases.Inventory.ReadModels;
using Animora.Domain.Inventory;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Converters;
using Animora.Persistence.Sqlite.Entities;
using Animora.Persistence.Sqlite.Options;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Animora.Persistence.Sqlite.Queries;

/// <summary>
/// Snapshot-joined inventory reads (C06). On-hand is taken from the maintained
/// <see cref="StockOnHandSnapshot"/> cache (O(1) per key), never a live SUM over the ledger. Tenant
/// and soft-delete filters are applied automatically by <c>ModelConventions</c>.
/// </summary>
internal static class InventoryStockQuery
{
    /// <summary>One product, at least one active variant at/below reorder; absent snapshot is zero.</summary>
    internal static IQueryable<Product> LowStockProducts(this IQueryable<Product> products, AnimoraLocalDbContext db) =>
        products.Where(LowStockPredicate(db));

    /// <summary>The complement of <see cref="LowStockProducts"/>: no active variant is at/below reorder.</summary>
    internal static IQueryable<Product> NotLowStockProducts(this IQueryable<Product> products, AnimoraLocalDbContext db)
    {
        var low = LowStockPredicate(db);
        return products.Where(Expression.Lambda<Func<Product, bool>>(Expression.Not(low.Body), low.Parameters));
    }

    private static Expression<Func<Product, bool>> LowStockPredicate(AnimoraLocalDbContext db) =>
        p => p.IsActive && p.Variants.Any(v => v.IsActive
            // Both sides are exact INTEGER x1000 columns (snapshot OnHandScaled vs the variant's ReorderLevelScaled):
            // a plain integer compare, no float cast, indexable by the snapshot's key.
            && (db.Set<StockOnHandSnapshot>().Where(s => s.TenantId == p.TenantId && s.ProductId == p.Id && s.VariantId == v.VariantId)
                .Select(s => (long?)s.OnHandScaled).FirstOrDefault() ?? 0L) <= (long)v.ReorderLevel.Value);
}

public sealed class InventoryQueries(AnimoraLocalDbContext db, IOptions<LocalStoreOptions> options)
    : IInventoryQueries
{
    private const int MaxCatalogScan = 1000; // Bounded scan before in-memory low-stock paging (the unfiltered/ok paths page in SQL).
    private const int MaxMovementHistory = 100;

    private LocalStoreOptions Options => options.Value;

    public async Task<PagedResult<ProductReadModel>> GetCatalogAsync(
        string? search, string? categoryId, bool lowStockOnly, int page, int pageSize, CancellationToken ct,
        bool okStockOnly = false)
    {
        var q = db.Set<Product>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            q = q.Where(p => EF.Functions.Like(p.Name, $"%{term}%") || EF.Functions.Like(p.Sku, $"%{term}%")
                || p.Variants.Any(v => EF.Functions.Like(v.Sku, $"%{term}%")));
        }
        if (!string.IsNullOrWhiteSpace(categoryId))
        {
            q = q.Where(p => p.CategoryId == categoryId);
        }

        if (!lowStockOnly)
        {
            // Common path: filter, order, count and slice in SQL; on-hand is loaded for the page's products only.
            if (okStockOnly) q = q.NotLowStockProducts(db);
            var paged = await q.OrderBy(p => p.Name).ThenBy(p => p.Id)
                .ToPagedAsync(Math.Max(1, page), pageSize, Options, ct);
            if (paged.Items.Count == 0)
            {
                return new PagedResult<ProductReadModel>([], paged.TotalCount, paged.Page, paged.PageSize);
            }

            var pageOnHand = await LoadOnHandAsync(paged.Items.Select(p => p.Id), ct);
            var pageRows = new List<ProductReadModel>(paged.Items.Count);
            for (var i = 0; i < paged.Items.Count; i++) pageRows.Add(ToReadModel(paged.Items[i], pageOnHand));
            return new PagedResult<ProductReadModel>(pageRows, paged.TotalCount, paged.Page, paged.PageSize);
        }

        q = q.LowStockProducts(db);

        // Low-stock path: bounded, stably-ordered scan; the exact at/below-reorder check runs on the
        // loaded snapshot values before the page is sliced.
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
            if (!IsLowStock(row))
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

    public async Task<IReadOnlyList<CategoryReadModel>> GetCategoriesAsync(CancellationToken ct)
    {
        var counts = await db.Set<Product>().AsNoTracking()
            .GroupBy(p => p.CategoryId)
            .Select(g => new { CategoryId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CategoryId, x => x.Count, ct);
        var rows = await db.Set<ProductCategory>().AsNoTracking().Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Code)
            .Select(c => new { c.Id, c.DisplayName, ParentId = c.ParentCategoryId ?? string.Empty })
            .ToListAsync(ct);
        var result = new List<CategoryReadModel>(rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            result.Add(new CategoryReadModel(rows[i].Id, rows[i].DisplayName, rows[i].ParentId,
                counts.TryGetValue(rows[i].Id, out var count) ? count : 0));
        }

        return result;
    }

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

    public async Task<bool> ProductSkuExistsAsync(string sku, string excludeProductId, CancellationToken ct) =>
        await db.Set<Product>().AsNoTracking()
            .AnyAsync(p => p.Sku == sku && p.Id != excludeProductId, ct);

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
        if (!product.IsActive) return false;
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
