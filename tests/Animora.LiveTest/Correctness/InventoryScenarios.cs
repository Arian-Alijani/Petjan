using Animora.Application.UseCases.Inventory;
using Animora.Domain.Inventory;

namespace Animora.LiveTest.Correctness;

/// <summary>Product categories, products + variants, price edits (incl. the aggregate-concurrency path), stock adjust / set, delete.</summary>
public static class InventoryScenarios
{
    public static IReadOnlyList<ScenarioDef> All { get; } =
    [
        new("Inventory", "Category create + rename converge", CategoryAsync),
        new("Inventory", "Product create (opening stock) → B converges; ledger entry appears", ProductCreateAsync),
        new("Inventory", "Price update on B → A converges with the new variant price", PriceUpdateAsync),
        new("Inventory", "Aggregate concurrency: two edits from the same product version → second settles Conflict", AggregateConcurrencyAsync),
        new("Inventory", "Stock adjust / set write the ledger; negative stock is refused", StockAsync),
        new("Inventory", "Delete a product with no stock movement → tombstone reaches B", DeleteAsync),
    ];

    private static ProductVariantInput Variant(ProductFixture p, long cost, long sale) =>
        new(p.VariantId, p.Sku + "-1", ProductVariant.DefaultAttributeName, "default", cost, sale, 0);

    private static async Task CategoryAsync(ScenarioContext c)
    {
        var created = await c.AcceptAsync(c.A, new CreateProductCategoryCommand(c.Name("category")));
        var id = ScenarioContext.IdOf(created);
        await c.AwaitConvergedAsync("ProductCategory", id);
        var renamed = c.Name("category-renamed");
        await c.AcceptAsync(c.B, new RenameProductCategoryCommand(id, renamed));
        await c.AwaitConvergedAsync("ProductCategory", id, r => r.Str("displayName") == renamed, "renamed");
    }

    private static async Task ProductCreateAsync(ScenarioContext c)
    {
        var p = await c.Shared.ProductAsync();
        var row = await c.AwaitConvergedAsync("Product", p.ProductId, r => r.Str("name") == p.Name, "created");
        c.Check(row.Str("categoryId") == p.CategoryId, "product replicated with a different category.");
        c.Check(row.FirstVariantSalePriceMinor() == 20_000, $"variant sale price replicated as {row.FirstVariantSalePriceMinor()}, expected 20000.");
        await c.AwaitFindAsync(c.B, "StockLedgerEntry", (_, r) => Fixtures.Mentions(r, p.ProductId), "opening-stock ledger entry");
    }

    private static async Task PriceUpdateAsync(ScenarioContext c)
    {
        var p = await c.Shared.ProductAsync();
        var current = await c.AwaitConvergedAsync("Product", p.ProductId);
        await c.AcceptAsync(c.B, new UpdateProductInfoCommand(p.ProductId, p.Name, 11_000, 25_000, [Variant(p, 11_000, 25_000)]), expectedServerVersion: current.Version);
        var row = await c.AwaitConvergedAsync("Product", p.ProductId, r => r.FirstVariantSalePriceMinor() == 25_000, "sale price 25000");
        c.Check(row.Version != current.Version, "product version did not move after a price edit.");
    }

    private static async Task AggregateConcurrencyAsync(ScenarioContext c)
    {
        var p = await c.Shared.ProductAsync();
        var v = (await c.AwaitConvergedAsync("Product", p.ProductId)).Version ?? throw ScenarioContext.Fail("product has no version token.");
        await c.AcceptAsync(c.A, new UpdateProductInfoCommand(p.ProductId, p.Name, 12_000, 26_000, [Variant(p, 12_000, 26_000)]), expectedServerVersion: v);
        await c.ConflictAsync(c.B, new UpdateProductInfoCommand(p.ProductId, p.Name, 13_000, 27_000, [Variant(p, 13_000, 27_000)]), v);

        var now = await c.AwaitRowAsync(c.B, "Product", p.ProductId, r => r.FirstVariantSalePriceMinor() == 26_000, "A's price");
        await c.AcceptAsync(c.B, new UpdateProductInfoCommand(p.ProductId, p.Name, 13_000, 27_000, [Variant(p, 13_000, 27_000)]), expectedServerVersion: now.Version);
        await c.AwaitConvergedAsync("Product", p.ProductId, r => r.FirstVariantSalePriceMinor() == 27_000, "B's retried price");
    }

    private static async Task StockAsync(ScenarioContext c)
    {
        var p = await c.Shared.ProductAsync();
        await c.PullAsync(c.B);
        var before = c.B.Replica.Rows("StockLedgerEntry").Count(r => Fixtures.Mentions(r.Value, p.ProductId));

        await c.AcceptAsync(c.A, new AdjustStockCommand(p.ProductId, p.VariantId, 5_000, AdjustmentReasons.Count, "lt +5"));
        await c.AcceptAsync(c.B, new SetStockCommand(p.ProductId, [new StockTargetLine(p.VariantId, 50_000)], AdjustmentReasons.Correction, "lt set 50"));

        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        var after = before;
        while (after < before + 2 && DateTimeOffset.UtcNow < deadline)
        {
            await c.PullAsync(c.B);
            after = c.B.Replica.Rows("StockLedgerEntry").Count(r => Fixtures.Mentions(r.Value, p.ProductId));
            if (after < before + 2) await Task.Delay(1000, c.Ct);
        }

        c.Check(after >= before + 2, $"expected 2 new stock ledger entries on B (adjust + set), saw {after - before}.");

        await c.RejectAsync(c.A, new AdjustStockCommand(p.ProductId, p.VariantId, -9_000_000, AdjustmentReasons.Damage, "lt would go negative"), "STOCK_WOULD_GO_NEGATIVE");
        await c.RejectAsync(c.A, new AdjustStockCommand(p.ProductId, p.VariantId, 1_000, "NOT_A_REASON", null));
    }

    private static async Task DeleteAsync(ScenarioContext c)
    {
        var categoryId = await c.Shared.CategoryIdAsync();
        var unit = await c.Shared.UnitCodeAsync();
        var sku = c.Name("sku-del");
        var created = await c.AcceptAsync(c.A, new CreateProductCommand(sku, c.Name("product-del"), categoryId, unit, ProductKind.Medicine, 1_000, 2_000,
            [new ProductVariantInput(null, sku + "-1", ProductVariant.DefaultAttributeName, "default", 1_000, 2_000, 0)]));
        var id = ScenarioContext.IdOf(created);
        await c.AwaitConvergedAsync("Product", id);
        await c.AcceptAsync(c.A, new DeleteProductCommand(id));
        await c.AwaitGoneAsync(c.B, "Product", id);
    }
}
