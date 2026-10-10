using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.Text;
using Animora.Application.UseCases.Inventory.ReadModels;
using Animora.Domain.Inventory;
using Animora.Persistence.PostgreSql.Context;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Repositories;

/// <summary>Tenant-filtered, movement-derived inventory reads; no absolute stock is stored.</summary>
internal static class InventoryStockQuery
{
    internal static IQueryable<Product> LowStockProducts(this IQueryable<Product> products, AnimoraServerDbContext db) => products.Where(LowStockPredicate(db));
    internal static IQueryable<Product> NotLowStockProducts(this IQueryable<Product> products, AnimoraServerDbContext db)
    {
        var low = LowStockPredicate(db);
        return products.Where(Expression.Lambda<Func<Product, bool>>(Expression.Not(low.Body), low.Parameters));
    }
    private static Expression<Func<Product, bool>> LowStockPredicate(AnimoraServerDbContext db) =>
        p => p.IsActive && p.Variants.Any(v => v.IsActive &&
            (db.Set<StockMovement>().Where(m => m.ProductId == p.Id && m.VariantId == v.VariantId)
                .Sum(m => (decimal?)m.Quantity.Value) ?? 0m) <= v.ReorderLevel.Value);
}

public sealed class InventoryQueries(AnimoraServerDbContext db)
    : IInventoryQueries
{
        private const int MaxMovementHistory = 100;

    // The «خدمات» catalogue lives in Sales/Cashier/Visits, not the warehouse. Its code is "SRV" and its rows
    // are stock-free <see cref="ProductKind.Service"/> products. Inventory («انبار و کالا») must never show the
    // service category chip/row nor the service products, so both are filtered out of every inventory read.
    private const string ServiceCategoryCode = "SRV";


    public async Task<PagedResult<ProductReadModel>> GetCatalogAsync(
        string? search, string? categoryId, bool lowStockOnly, int page, int pageSize, CancellationToken ct,
        bool okStockOnly = false)
    {
        var q = db.Set<Product>().AsNoTracking().Where(p => p.Kind != ProductKind.Service);
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

        if (lowStockOnly) q = q.LowStockProducts(db);
        else if (okStockOnly) q = q.NotLowStockProducts(db);
        var paged = await q.OrderBy(p => p.Name).ThenBy(p => p.Id).ToPagedAsync(page, pageSize, ct);
        var onHand = await LoadOnHandAsync(paged.Items.Select(p => p.Id), ct);
        return new PagedResult<ProductReadModel>(paged.Items.Select(p => ToReadModel(p, onHand)).ToArray(),
            paged.TotalCount, paged.Page, paged.PageSize);
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
            .Where(p => p.Kind != ProductKind.Service)
            .GroupBy(p => p.CategoryId)
            .Select(g => new { CategoryId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CategoryId, x => x.Count, ct);
        var rows = await db.Set<ProductCategory>().AsNoTracking()
            .Where(c => c.IsActive && c.Code != ServiceCategoryCode)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Code).Take(PageRules.MaxPageSize)
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
        var paged = await q.ToPagedAsync(page, pageSize, ct);
        var items = new List<MovementReadModel>(paged.Items.Count);
        for (var i = 0; i < paged.Items.Count; i++)
        {
            items.Add(ToMovement(paged.Items[i]));
        }
        return new PagedResult<MovementReadModel>(items, paged.TotalCount, paged.Page, paged.PageSize);
    }

    public async Task<long> GetOnHandScaledAsync(string productId, string variantId, CancellationToken ct)
    {
        await StockResourceLock.AcquireAsync(db, productId, variantId, ct);
        var quantity = await db.Set<StockMovement>().AsNoTracking()
            .Where(m => m.ProductId == productId && m.VariantId == variantId)
            .SumAsync(m => (decimal?)m.Quantity.Value, ct) ?? 0m;
        var staged = db.Set<StockMovement>().Local.Where(m => db.Entry(m).State == EntityState.Added
            && m.ProductId == productId && m.VariantId == variantId).Sum(m => m.Quantity.Value);
        return Scaled(quantity + staged);
    }

    public async Task<bool> ProductSkuExistsAsync(string sku, string excludeProductId, CancellationToken ct) =>
        await db.Set<Product>().AsNoTracking()
            .AnyAsync(p => p.Sku == sku && p.Id != excludeProductId, ct);

    public async Task<long> GetProductOnHandScaledAsync(string productId, CancellationToken ct)
    {
        var quantity = await db.Set<StockMovement>().AsNoTracking().Where(m => m.ProductId == productId)
            .SumAsync(m => (decimal?)m.Quantity.Value, ct) ?? 0m;
        return Scaled(quantity);
    }

    public Task<int> CountMovementsSinceAsync(string productId, DateTimeOffset sinceUtc, CancellationToken ct) =>
        db.Set<StockMovement>().AsNoTracking()
            .CountAsync(m => m.ProductId == productId && m.OccurredUtc >= sinceUtc, ct);

    private async Task<Dictionary<(string ProductId, string VariantId), long>> LoadOnHandAsync(
        IEnumerable<string> productIds, CancellationToken ct)
    {
        var ids = productIds.Distinct(StringComparer.Ordinal).ToArray();
        var rows = await db.Set<StockMovement>().AsNoTracking().Where(m => ids.Contains(m.ProductId))
            .GroupBy(m => new { m.ProductId, m.VariantId })
            .Select(g => new { g.Key.ProductId, g.Key.VariantId, Quantity = g.Sum(m => m.Quantity.Value) }).ToListAsync(ct);
        return rows.ToDictionary(r => (r.ProductId, r.VariantId), r => Scaled(r.Quantity));
    }

    private static long Scaled(decimal value) => checked((long)(value * 1000m));

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
                Scaled(v.ReorderLevel.Value),
                v.Barcode, v.IsActive, scaled));
        }
        return new ProductReadModel(
            product.Id, product.Sku, product.Name, product.Brand ?? string.Empty,
            product.CategoryId, product.Kind.ToString(), product.UnitCode, product.Notes,
            product.IsActive, variants);
    }

    private static MovementReadModel ToMovement(StockMovement m) => new(
        m.Id, m.ProductId, m.VariantId, m.Kind.ToString(),
        Scaled(m.Quantity.Value), m.UnitCost.Minor,
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
