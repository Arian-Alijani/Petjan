using Animora.Application.UseCases.Purchasing;
using Animora.Domain.Inventory;

namespace Animora.LiveTest.Correctness;

/// <summary>Suppliers, purchase orders (create / submit / cancel) and goods receipts that write the stock ledger.</summary>
public static class PurchasingScenarios
{
    public static IReadOnlyList<ScenarioDef> All { get; } =
    [
        new("Purchasing", "Supplier create converges; same name returns the existing supplier", SupplierAsync),
        new("Purchasing", "Order create (submitted) → receipt against the order → ledger entry on B", OrderAndReceiptAsync),
        new("Purchasing", "Draft order can be cancelled; cancelling twice is refused", CancelAsync),
    ];

    private static async Task SupplierAsync(ScenarioContext c)
    {
        var name = c.Name("supplier");
        var first = await c.AcceptAsync(c.A, new CreateSupplierCommand(name, null, 14, ScenarioContext.NextPhone()));
        var id = ScenarioContext.IdOf(first);
        var row = await c.AwaitConvergedAsync("Supplier", id, r => r.Str("name") == name, "created");
        c.Check(row.Num("paymentTermDays") == 14, $"payment terms replicated as {row.Num("paymentTermDays")}, expected 14.");

        var again = await c.AcceptAsync(c.B, new CreateSupplierCommand(name, null, 14, null));
        c.Check(ScenarioContext.IdOf(again) == id, $"creating a supplier with the same name returned a new id ({ScenarioContext.IdOf(again)}) instead of the existing {id}.");
    }

    private static async Task OrderAndReceiptAsync(ScenarioContext c)
    {
        var supplier = await c.Shared.SupplierIdAsync();
        var p = await c.Shared.ProductAsync();
        await c.PullAsync(c.B);
        var before = c.B.Replica.Rows("StockLedgerEntry").Count(r => Fixtures.Mentions(r.Value, p.ProductId));

        var order = await c.AcceptAsync(c.A, new CreatePurchaseOrderCommand(supplier, [new PurchaseOrderLineInput(p.ProductId, p.VariantId, 10_000, 9_000)],
            DateTimeOffset.UtcNow.AddDays(3), "lt order", Submit: true));
        var orderId = ScenarioContext.IdOf(order);

        await c.AcceptAsync(c.B, new ReceiveGoodsCommand(supplier, [new GoodsReceiptLineInput(p.ProductId, p.VariantId, 10_000, 9_000, "LT-LOT")], orderId, "lt-inv-" + c.RunId));

        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        var after = before;
        while (after <= before && DateTimeOffset.UtcNow < deadline)
        {
            await c.PullAsync(c.A);
            after = c.A.Replica.Rows("StockLedgerEntry").Count(r => Fixtures.Mentions(r.Value, p.ProductId));
            if (after <= before) await Task.Delay(1000, c.Ct);
        }

        c.Check(after > before, "goods receipt did not produce a stock ledger entry that reached client A.");
        await c.RejectAsync(c.A, new ReceiveGoodsCommand(supplier, [], null, null));
    }

    private static async Task CancelAsync(ScenarioContext c)
    {
        var supplier = await c.Shared.SupplierIdAsync();
        var p = await c.Shared.ProductAsync();
        var order = await c.AcceptAsync(c.A, new CreatePurchaseOrderCommand(supplier, [new PurchaseOrderLineInput(p.ProductId, p.VariantId, 1_000, 9_000)]));
        var id = ScenarioContext.IdOf(order);
        await c.AcceptAsync(c.B, new CancelPurchaseOrderCommand(id, "lt cancel"));
        await c.RejectAsync(c.A, new CancelPurchaseOrderCommand(id, "lt cancel again"));
    }
}
