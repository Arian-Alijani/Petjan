using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.UseCases.Inventory;
using Animora.Application.UseCases.Inventory.ReadModels;
using Animora.Application.UseCases.Purchasing;
using Animora.Domain.Inventory;
using Animora.Domain.Purchasing;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Domain.ValueObjects;
using Xunit;

namespace Animora.Application.Tests;

/// <summary>
/// C06 use-case rules for inventory adjustments, product delete, and goods receipt (T12). Uses in-memory
/// fakes over the application ports — the persistence-side snapshot integrity is proven separately in
/// <c>StockSnapshotIntegrityTests</c>.
/// </summary>
public sealed class InventoryUseCaseTests
{
    private const string Tenant = "T-LOCAL-DEFAULT";
    private const string Unit = "PCS";
    private static readonly DateTimeOffset Now = new(2026, 3, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly CommandContext Context =
        new(Tenant, "user", "device", "operation", "correlation", null, null, null, Now);
    private static readonly IClock Clock = new InventoryClock(Now);

    private static Product NewProduct(out string variantId)
    {
        variantId = "V-1";
        var variant = new ProductVariant(variantId, "SKU-V1", "Size", "Small",
            Money.Irt(1000), Money.Irt(2000), new Quantity(5m, Unit), null, true);
        return Product.Create(Tenant, "SKU-1", "Amoxicillin", "CAT-MED", ProductKind.Medicine, Unit,
            Money.Irt(1000), Money.Irt(2000), new[] { variant }).Value!;
    }

    [Fact]
    public async Task Adjust_WithUnknownReason_IsRejected()
    {
        var product = NewProduct(out var variantId);
        var handler = new AdjustStockCommandHandler(
            new ProductRepositoryFake(product), new StockMovementRepositoryFake(),
            new InventoryQueriesFake(), Clock);

        var result = await handler.HandleAsync(
            new AdjustStockCommand(product.Id, variantId, 1000L, "Made-up-reason"), Context, default);

        Assert.Equal(ErrorCodes.AdjustmentReasonInvalid, result.Error);
    }

    [Fact]
    public async Task Adjust_ThatWouldGoBelowZero_IsRejected()
    {
        var product = NewProduct(out var variantId);
        var movements = new StockMovementRepositoryFake();
        // On-hand is 3 (×1000); a Damage of -5 (×1000) would drive it to -2.
        var queries = new InventoryQueriesFake { OnHandScaled = 3_000L };
        var handler = new AdjustStockCommandHandler(
            new ProductRepositoryFake(product), movements, queries, Clock);

        var result = await handler.HandleAsync(
            new AdjustStockCommand(product.Id, variantId, -5_000L, AdjustmentReasons.Damage), Context, default);

        Assert.Equal(ErrorCodes.StockWouldGoNegative, result.Error);
        Assert.Empty(movements.Added);
    }

    [Fact]
    public async Task Adjust_ExactCountToZero_IsAllowed_ButNotBelow()
    {
        var product = NewProduct(out var variantId);
        var movements = new StockMovementRepositoryFake();
        var queries = new InventoryQueriesFake { OnHandScaled = 4_000L };
        var handler = new AdjustStockCommandHandler(
            new ProductRepositoryFake(product), movements, queries, Clock);

        // A physical count may correct the on-hand exactly down to zero.
        var toZero = await handler.HandleAsync(
            new AdjustStockCommand(product.Id, variantId, -4_000L, AdjustmentReasons.Count), Context, default);
        Assert.True(toZero.IsSuccess);
        Assert.Single(movements.Added);
        Assert.Equal(MovementKind.AdjustmentOut, movements.Added[0].Kind);

        // But a count can never take it strictly below zero.
        var belowZero = await handler.HandleAsync(
            new AdjustStockCommand(product.Id, variantId, -4_001L, AdjustmentReasons.Count), Context, default);
        Assert.Equal(ErrorCodes.StockWouldGoNegative, belowZero.Error);
        Assert.Single(movements.Added);
    }

    [Fact]
    public async Task Adjust_MissingVariant_ReturnsVariantNotFound()
    {
        var product = NewProduct(out _);
        var handler = new AdjustStockCommandHandler(
            new ProductRepositoryFake(product), new StockMovementRepositoryFake(),
            new InventoryQueriesFake(), Clock);

        var result = await handler.HandleAsync(
            new AdjustStockCommand(product.Id, "V-missing", 1_000L, AdjustmentReasons.Correction), Context, default);

        Assert.Equal(ErrorCodes.VariantNotFound, result.Error);
    }

    [Fact]
    public async Task Delete_WithOnHandStock_IsRefused()
    {
        var product = NewProduct(out _);
        var queries = new InventoryQueriesFake { ProductOnHandScaled = 2_000L };
        var handler = new DeleteProductCommandHandler(new ProductRepositoryFake(product), queries, Clock);

        var result = await handler.HandleAsync(new DeleteProductCommand(product.Id), Context, default);

        Assert.Equal(ErrorCodes.ProductHasStock, result.Error);
        Assert.True(product.IsActive);
        Assert.False(product.Deleted);
    }

    [Fact]
    public async Task Delete_WithRecentMovement_IsRefused()
    {
        var product = NewProduct(out _);
        var queries = new InventoryQueriesFake { ProductOnHandScaled = 0L, MovementsSince = 3 };
        var handler = new DeleteProductCommandHandler(new ProductRepositoryFake(product), queries, Clock);

        var result = await handler.HandleAsync(new DeleteProductCommand(product.Id), Context, default);

        Assert.Equal(ErrorCodes.ProductHasStock, result.Error);
    }

    [Fact]
    public async Task Delete_QuietProduct_SoftDeletes()
    {
        var product = NewProduct(out _);
        var queries = new InventoryQueriesFake { ProductOnHandScaled = 0L, MovementsSince = 0 };
        var handler = new DeleteProductCommandHandler(new ProductRepositoryFake(product), queries, Clock);

        var result = await handler.HandleAsync(new DeleteProductCommand(product.Id), Context, default);

        Assert.True(result.IsSuccess);
        Assert.False(product.IsActive);
        Assert.True(product.Deleted);
    }

    [Fact]
    public async Task ReceiveGoods_OverOrderedQuantity_IsRejectedUnlessAllowed()
    {
        var product = NewProduct(out var variantId);
        var order = SubmittedOrderFor(product, variantId, orderedScaled: 5_000L);
        var purchasing = new PurchasingRepositoryFake(order);
        var movements = new StockMovementRepositoryFake();
        var handler = new ReceiveGoodsCommandHandler(
            purchasing, new ProductRepositoryFake(product), movements, new PurchasingQueriesFake(), Clock);

        // Receiving 7 against an ordered 5 is over-receipt.
        var rejected = await handler.HandleAsync(new ReceiveGoodsCommand(
            "S-1", new[] { new GoodsReceiptLineInput(product.Id, variantId, 7_000L, 1_000L) },
            PurchaseOrderId: order.Id), Context, default);
        Assert.Equal(ErrorCodes.ReceiptExceedsOrder, rejected.Error);
        Assert.Empty(movements.Added);
        Assert.Empty(purchasing.Receipts);
    }

    [Fact]
    public async Task ReceiveGoods_AppendsReceiptMovements_AndAdvancesOrder()
    {
        var product = NewProduct(out var variantId);
        var order = SubmittedOrderFor(product, variantId, orderedScaled: 10_000L);
        var purchasing = new PurchasingRepositoryFake(order);
        var movements = new StockMovementRepositoryFake();
        var handler = new ReceiveGoodsCommandHandler(
            purchasing, new ProductRepositoryFake(product), movements, new PurchasingQueriesFake(), Clock);

        var result = await handler.HandleAsync(new ReceiveGoodsCommand(
            "S-1", new[] { new GoodsReceiptLineInput(product.Id, variantId, 4_000L, 1_500L) },
            PurchaseOrderId: order.Id), Context, default);

        Assert.True(result.IsSuccess);
        var movement = Assert.Single(movements.Added);
        Assert.Equal(MovementKind.Receipt, movement.Kind);
        Assert.Equal(4m, movement.Quantity.Value);
        Assert.Equal(1_500L, movement.UnitCost.Minor); // The receipt carries the unit cost onto the ledger.
        Assert.Single(purchasing.Receipts);
        Assert.Equal(PurchaseOrderState.PartiallyReceived, order.State);
    }

    private static PurchaseOrder SubmittedOrderFor(Product product, string variantId, long orderedScaled)
    {
        var order = PurchaseOrder.Create(Tenant, "PO-00001", "S-1", Now, "user").Value!;
        var line = new PurchaseOrderLine(
            "L-1", product.Id, variantId,
            new Quantity(orderedScaled / 1000m, Unit), Quantity.Zero(Unit),
            Money.Irt(1_000L), 0m, 0m, null, null);
        Assert.True(order.AddLine(line).IsSuccess);
        Assert.True(order.Submit(Clock).IsSuccess);
        return order;
    }
}

internal sealed class InventoryClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow => now;
}

internal sealed class ProductRepositoryFake(params Product[] products) : IProductRepository
{
    private readonly Dictionary<string, Product> _products = products.ToDictionary(p => p.Id);
    public Task<Product?> FindAsync(string id, CancellationToken ct = default) => Task.FromResult(_products.GetValueOrDefault(id));
    public Task<Product> RequireAsync(string id, CancellationToken ct = default) => Task.FromResult(_products[id]);
    public void Add(Product entity) => _products[entity.Id] = entity;
    public void AddRange(IEnumerable<Product> entities)
    {
        foreach (var entity in entities) Add(entity);
    }
}

internal sealed class StockMovementRepositoryFake : IStockMovementRepository
{
    public List<StockMovement> Added { get; } = [];
    public Task<StockMovement?> FindAsync(string id, CancellationToken ct = default) =>
        Task.FromResult(Added.FirstOrDefault(m => m.Id == id));
    public Task<StockMovement> RequireAsync(string id, CancellationToken ct = default) =>
        Task.FromResult(Added.First(m => m.Id == id));
    public void Add(StockMovement entity) => Added.Add(entity);
    public void AddRange(IEnumerable<StockMovement> entities) => Added.AddRange(entities);
}

internal sealed class PurchasingRepositoryFake(params PurchaseOrder[] orders) : IPurchasingRepository
{
    private readonly Dictionary<string, PurchaseOrder> _orders = orders.ToDictionary(o => o.Id);
    public List<GoodsReceipt> Receipts { get; } = [];
    public Task<PurchaseOrder?> FindAsync(string id, CancellationToken ct = default) => Task.FromResult(_orders.GetValueOrDefault(id));
    public Task<PurchaseOrder> RequireAsync(string id, CancellationToken ct = default) => Task.FromResult(_orders[id]);
    public void Add(PurchaseOrder entity) => _orders[entity.Id] = entity;
    public void AddRange(IEnumerable<PurchaseOrder> entities)
    {
        foreach (var entity in entities) Add(entity);
    }
    public void AddReceipt(GoodsReceipt receipt) => Receipts.Add(receipt);
    public Task<GoodsReceipt?> FindReceiptAsync(string id, CancellationToken ct = default) =>
        Task.FromResult(Receipts.FirstOrDefault(r => r.Id == id));
}

internal sealed class InventoryQueriesFake : IInventoryQueries
{
    public long OnHandScaled { get; set; }
    public long ProductOnHandScaled { get; set; }
    public int MovementsSince { get; set; }

    public Task<PagedResult<ProductReadModel>> GetCatalogAsync(
        string? search, string? categoryId, bool lowStockOnly, int page, int pageSize, CancellationToken ct,
        bool okStockOnly = false) =>
        Task.FromResult(new PagedResult<ProductReadModel>([], 0, page, pageSize));
    public Task<ProductDetailReadModel?> GetDetailAsync(string productId, CancellationToken ct) =>
        Task.FromResult<ProductDetailReadModel?>(null);
    public Task<IReadOnlyList<CategoryReadModel>> GetCategoriesAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<CategoryReadModel>>([]);
    public Task<PagedResult<MovementReadModel>> GetMovementHistoryAsync(
        string productId, int page, int pageSize, CancellationToken ct) =>
        Task.FromResult(new PagedResult<MovementReadModel>([], 0, page, pageSize));
    public Task<long> GetOnHandScaledAsync(string productId, string variantId, CancellationToken ct) =>
        Task.FromResult(OnHandScaled);
    public Task<long> GetProductOnHandScaledAsync(string productId, CancellationToken ct) =>
        Task.FromResult(ProductOnHandScaled);
    public Task<bool> ProductSkuExistsAsync(string sku, string excludeProductId, CancellationToken ct) =>
        Task.FromResult(false);
    public Task<int> CountMovementsSinceAsync(string productId, DateTimeOffset sinceUtc, CancellationToken ct) =>
        Task.FromResult(MovementsSince);
}

internal sealed class PurchasingQueriesFake : IPurchasingQueries
{
    public int Orders { get; set; }
    public int Receipts { get; set; }
    public Task<Animora.Application.UseCases.Purchasing.ReadModels.PurchasingSnapshotReadModel> GetSnapshotAsync(
        DateTimeOffset nowUtc, CancellationToken ct) =>
        Task.FromResult(new Animora.Application.UseCases.Purchasing.ReadModels.PurchasingSnapshotReadModel(
            0, 0, 0, CurrencyScale.Irt, []));
    public Task<int> CountOrdersAsync(CancellationToken ct) => Task.FromResult(Orders);
    public Task<int> CountReceiptsAsync(CancellationToken ct) => Task.FromResult(Receipts);
}
