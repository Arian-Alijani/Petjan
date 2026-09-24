using System;
using System.Collections.Generic;
using Animora.Domain.Primitives;
using Animora.Domain.Purchasing;
using Animora.Domain.Results;
using Animora.Domain.ValueObjects;
using Xunit;

namespace Animora.Domain.Tests;

/// <summary>
/// The purchase-order state machine, the line lock after submission (<c>PO_LOCKED</c>), the
/// totals-match guard (<c>PO_TOTAL_MISMATCH</c>) and partial receipt.
/// </summary>
public sealed class PurchaseOrderTests
{
    private static readonly DateTimeOffset Ordered = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private static readonly HashSet<(PurchaseOrderState From, PurchaseOrderState To)> Allowed =
    [
        (PurchaseOrderState.Draft, PurchaseOrderState.Submitted),
        (PurchaseOrderState.Draft, PurchaseOrderState.Cancelled),
        (PurchaseOrderState.Submitted, PurchaseOrderState.PartiallyReceived),
        (PurchaseOrderState.Submitted, PurchaseOrderState.Received),
        (PurchaseOrderState.Submitted, PurchaseOrderState.Cancelled),
        (PurchaseOrderState.PartiallyReceived, PurchaseOrderState.PartiallyReceived),
        (PurchaseOrderState.PartiallyReceived, PurchaseOrderState.Received),
        (PurchaseOrderState.PartiallyReceived, PurchaseOrderState.Closed),
        (PurchaseOrderState.Received, PurchaseOrderState.Closed),
    ];

    private static PurchaseOrderLine Line(string variant, decimal ordered, long unitCost = 1_000) =>
        new(IdGenerator.NewId(Ordered), "P1", variant, new Quantity(ordered, "EA"), Quantity.Zero("EA"),
            Money.Irt(unitCost), 0m, 0m, null, null);

    private static GoodsReceipt Receipt(string variant, decimal quantity) =>
        GoodsReceipt.Create("T1", "GR-1", "SUP1", Ordered, "U1").Value!;

    [Fact]
    public void AllStatePairs_MatchTheTable()
    {
        var states = Enum.GetValues<PurchaseOrderState>();
        foreach (var from in states)
        {
            foreach (var to in states)
            {
                Assert.Equal(Allowed.Contains((from, to)), PurchaseOrder.IsTransitionAllowed(from, to));
            }
        }
    }

    [Fact]
    public void Submit_ComputesAndMatchesTotals()
    {
        var clock = new FakeClock(Ordered);
        var order = PurchaseOrder.Create("T1", "PO-1", "SUP1", Ordered, "U1").Value!;
        order.AddLine(Line("V1", 10m, 1_000));

        Assert.Equal(Money.Irt(10_000), order.Subtotal);
        Assert.Equal(Money.Irt(10_000), order.GrandTotal);
        Assert.True(order.Submit(clock).IsSuccess);
        Assert.Equal(PurchaseOrderState.Submitted, order.State);
    }

    [Fact]
    public void Lines_AreLockedAfterSubmit()
    {
        var clock = new FakeClock(Ordered);
        var order = PurchaseOrder.Create("T1", "PO-2", "SUP1", Ordered, "U1").Value!;
        order.AddLine(Line("V1", 10m));
        order.Submit(clock);

        var add = order.AddLine(Line("V2", 1m));
        Assert.True(add.IsFailure);
        Assert.Equal(ErrorCodes.PurchaseOrderLocked, add.Error);

        var remove = order.RemoveLine(order.Lines[0].LineId);
        Assert.True(remove.IsFailure);
        Assert.Equal(ErrorCodes.PurchaseOrderLocked, remove.Error);
    }

    [Fact]
    public void PartialReceipt_MovesToPartiallyReceived()
    {
        var clock = new FakeClock(Ordered);
        var order = PurchaseOrder.Create("T1", "PO-3", "SUP1", Ordered, "U1").Value!;
        order.AddLine(Line("V1", 10m));
        order.Submit(clock);

        var receipt = Receipt("V1", 4m);
        receipt.AddLine(new GoodsReceiptLine("L1", "P1", "V1", new Quantity(4m, "EA"),
            Money.Irt(1_000), null, null));

        var applied = order.ApplyReceipt(receipt);

        Assert.True(applied.IsSuccess);
        Assert.Equal(PurchaseOrderState.PartiallyReceived, order.State);
        Assert.Equal(4m, order.Lines[0].ReceivedQuantity.Value);
    }

    [Fact]
    public void FullReceipt_MovesToReceived()
    {
        var clock = new FakeClock(Ordered);
        var order = PurchaseOrder.Create("T1", "PO-4", "SUP1", Ordered, "U1").Value!;
        order.AddLine(Line("V1", 10m));
        order.Submit(clock);

        var receipt = Receipt("V1", 10m);
        receipt.AddLine(new GoodsReceiptLine("L1", "P1", "V1", new Quantity(10m, "EA"),
            Money.Irt(1_000), null, null));

        Assert.True(order.ApplyReceipt(receipt).IsSuccess);
        Assert.Equal(PurchaseOrderState.Received, order.State);
    }

    [Fact]
    public void EmptyOrder_CannotBeSubmitted()
    {
        var clock = new FakeClock(Ordered);
        var order = PurchaseOrder.Create("T1", "PO-5", "SUP1", Ordered, "U1").Value!;

        var result = order.Submit(clock);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.ValidationFailed, result.Error);
    }

    [Fact]
    public void CancelledOrder_RefusesFurtherTransitions()
    {
        var order = PurchaseOrder.Create("T1", "PO-6", "SUP1", Ordered, "U1").Value!;
        Assert.True(order.Cancel("no longer needed").IsSuccess);

        var result = order.Close();
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.PurchaseOrderInvalidTransition, result.Error);
    }
}
