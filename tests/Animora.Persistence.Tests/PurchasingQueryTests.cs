using System;
using System.Linq;
using System.Threading.Tasks;
using Animora.Domain.Purchasing;
using Animora.Domain.ValueObjects;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Queries;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Persistence.Tests;

/// <summary>
/// C06 T12: the purchasing snapshot's KPI aggregates match the seeded orders/receipts, and the whole
/// snapshot is computed in ≤3 real SQL commands (verified by counting commands, not SELECT substrings).
/// </summary>
public sealed class PurchasingQueryTests
{
    private const string Unit = "PCS";
    private static readonly DateTimeOffset Now = new(2026, 3, 15, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Snapshot_KpisMatchSeededData()
    {
        using var fixture = new LocalStoreTestFixture(now: Now);
        await fixture.MigrateAsync();
        await SeedAsync(fixture);

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var snapshot = await new PurchasingQueries(db).GetSnapshotAsync(Now, default);

        // Submitted (PO-A) + PartiallyReceived (PO-B) are still active; the Cancelled PO-C is not.
        Assert.Equal(2, snapshot.ActiveOrderCount);
        // Outstanding supplier debt = value of goods received but not closed = only PO-B (PartiallyReceived).
        Assert.Equal(20_000L, snapshot.SupplierDebtMinor);
        // Month-to-date spend = the one posted goods receipt this month (5 units × 2000).
        Assert.Equal(10_000L, snapshot.MonthToDateSpendMinor);
        Assert.Equal(3, snapshot.Orders.Count);
        Assert.Equal(CurrencyScale.Irt, snapshot.Currency);

        // The order rows carry supplier name + payment term (joined in the second SELECT).
        var partial = snapshot.Orders.Single(o => o.State == (int)PurchaseOrderState.PartiallyReceived);
        Assert.Equal("Acme Vet Supply", partial.SupplierName);
        Assert.Equal(30, partial.PaymentTermDays);
    }

    [Fact]
    public async Task Snapshot_IssuesAtMostThreeSelects()
    {
        using var fixture = new LocalStoreTestFixture(now: Now);
        await fixture.MigrateAsync();
        await SeedAsync(fixture);

        var (context, counter) = fixture.CreateCountingContext();
        using (context)
        {
            await context.Database.OpenConnectionAsync();
            counter.Reset();
            _ = await new PurchasingQueries(context).GetSnapshotAsync(Now, default);
            Assert.True(counter.Count <= 3, $"expected ≤3 SELECTs, saw {counter.Count}.");
        }
    }

    private static async Task SeedAsync(LocalStoreTestFixture fixture)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();

        var supplier = Supplier.Create(fixture.TenantId, "SUP-1", "Acme Vet Supply", paymentTermDays: 30).Value!;
        db.Add(supplier);

        // PO-A — Submitted, still fully outstanding (active, not yet contributing to debt).
        var orderA = NewOrder(fixture, "PO-00001", supplier.Id, orderedScaled: 10_000L, unitCostMinor: 1_000L);
        Assert.True(orderA.Submit(fixture.Clock).IsSuccess);
        db.Add(orderA);

        // PO-B — Submitted, then a partial receipt is applied → PartiallyReceived + supplier debt.
        var orderB = NewOrder(fixture, "PO-00002", supplier.Id, orderedScaled: 10_000L, unitCostMinor: 2_000L);
        Assert.True(orderB.Submit(fixture.Clock).IsSuccess);
        var receipt = GoodsReceipt.Create(fixture.TenantId, "GR-00001", supplier.Id, Now, "U1",
            purchaseOrderId: orderB.Id).Value!;
        Assert.True(receipt.AddLine(new GoodsReceiptLine(
            "GRL-1", orderB.Lines[0].ProductId, orderB.Lines[0].VariantId,
            new Quantity(5m, Unit), Money.Irt(2_000L), null, null)).IsSuccess);
        Assert.True(receipt.Post(fixture.Clock).IsSuccess);
        Assert.True(orderB.ApplyReceipt(receipt).IsSuccess);
        Assert.Equal(PurchaseOrderState.PartiallyReceived, orderB.State);
        db.Add(orderB);
        db.Add(receipt);

        // PO-C — Cancelled: neither active nor a debt.
        var orderC = NewOrder(fixture, "PO-00003", supplier.Id, orderedScaled: 4_000L, unitCostMinor: 5_000L);
        Assert.True(orderC.Cancel("supplier out of stock").IsSuccess);
        db.Add(orderC);

        await db.SaveChangesAsync();
    }

    private static PurchaseOrder NewOrder(
        LocalStoreTestFixture fixture, string number, string supplierId, long orderedScaled, long unitCostMinor)
    {
        var order = PurchaseOrder.Create(fixture.TenantId, number, supplierId, Now, "U1").Value!;
        var line = new PurchaseOrderLine(
            $"L-{number}", "P1", "V1",
            new Quantity(orderedScaled / 1000m, Unit), Quantity.Zero(Unit),
            Money.Irt(unitCostMinor), 0m, 0m, null, null);
        Assert.True(order.AddLine(line).IsSuccess);
        return order;
    }
}
