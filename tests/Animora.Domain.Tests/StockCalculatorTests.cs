using System;
using System.Collections.Generic;
using Animora.Domain.Inventory;
using Animora.Domain.Results;
using Animora.Domain.ValueObjects;
using Xunit;

namespace Animora.Domain.Tests;

/// <summary>
/// The single on-hand algorithm (R-B23): signed sum of the movement ledger, moving weighted-average
/// cost, sufficiency guard and earliest inbound expiry. All pure; a mixed-unit list is a defect.
/// </summary>
public sealed class StockCalculatorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private static StockMovement Movement(MovementKind kind, decimal quantity, long unitCostMinor,
        DateTimeOffset? expiry = null, int atMinutes = 0) =>
        StockMovement.Create("T1", "P1", "V1", kind, new Quantity(quantity, "EA"),
            Money.Irt(unitCostMinor), T0.AddMinutes(atMinutes), "U1", expiryUtc: expiry).Value!;

    [Fact]
    public void OnHand_IsTheSignedSumOfFiveMovements()
    {
        var movements = new List<StockMovement>
        {
            Movement(MovementKind.Receipt, 100m, 1_000),
            Movement(MovementKind.Receipt, 50m, 1_000),
            Movement(MovementKind.Sale, -30m, 1_000),
            Movement(MovementKind.Loss, -5m, 1_000),
            Movement(MovementKind.AdjustmentIn, 10m, 1_000),
        };

        var onHand = StockCalculator.OnHand(movements);

        Assert.Equal(125m, onHand.Value);
        Assert.Equal("EA", onHand.UnitCode);
    }

    [Fact]
    public void OnHand_OfEmptyLedger_IsZeroEach()
    {
        var onHand = StockCalculator.OnHand([]);

        Assert.Equal(0m, onHand.Value);
        Assert.Equal("EA", onHand.UnitCode);
    }

    [Fact]
    public void WeightedAverageCost_AfterTwoReceiptsAndOneSale()
    {
        // 100 @ 1000 -> avg 1000; 100 @ 2000 -> value 300000 / qty 200 = 1500;
        // sale of 50 consumes at 1500, leaving 150 @ 1500.
        var movements = new List<StockMovement>
        {
            Movement(MovementKind.Receipt, 100m, 1_000),
            Movement(MovementKind.Receipt, 100m, 2_000),
            Movement(MovementKind.Sale, -50m, 9_999),
        };

        var average = StockCalculator.WeightedAverageCost(movements);

        Assert.Equal(Money.Irt(1_500), average);
    }

    [Fact]
    public void WeightedAverageCost_OfEmptyLedger_IsZero()
    {
        Assert.Equal(Money.ZeroIrt, StockCalculator.WeightedAverageCost([]));
    }

    [Fact]
    public void EnsureSufficient_AllowsNegativeWhenConfigured()
    {
        var onHand = new Quantity(5m, "EA");
        var requested = new Quantity(10m, "EA");

        Assert.True(StockCalculator.EnsureSufficient(onHand, requested, allowNegative: true).IsSuccess);

        var blocked = StockCalculator.EnsureSufficient(onHand, requested, allowNegative: false);
        Assert.True(blocked.IsFailure);
        Assert.Equal(ErrorCodes.InsufficientStock, blocked.Error);
    }

    [Fact]
    public void EnsureSufficient_WithUnitMismatch_ReturnsUnitMismatch()
    {
        var result = StockCalculator.EnsureSufficient(new Quantity(5m, "EA"), new Quantity(1m, "ML"), false);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.UnitMismatch, result.Error);
    }

    [Fact]
    public void IsBelowReorder_TriggersOnlyForAPositiveLevel()
    {
        Assert.True(StockCalculator.IsBelowReorder(new Quantity(5m, "EA"), new Quantity(5m, "EA")));
        Assert.True(StockCalculator.IsBelowReorder(new Quantity(3m, "EA"), new Quantity(5m, "EA")));
        Assert.False(StockCalculator.IsBelowReorder(new Quantity(6m, "EA"), new Quantity(5m, "EA")));
        Assert.False(StockCalculator.IsBelowReorder(new Quantity(0m, "EA"), new Quantity(0m, "EA")));
    }

    [Fact]
    public void EarliestExpiry_IgnoresOutboundMovements()
    {
        var later = T0.AddDays(90);
        var sooner = T0.AddDays(30);
        var movements = new List<StockMovement>
        {
            Movement(MovementKind.Receipt, 10m, 1_000, expiry: later),
            Movement(MovementKind.Sale, -5m, 1_000, expiry: T0.AddDays(1)), // outbound: ignored
            Movement(MovementKind.Receipt, 10m, 1_000, expiry: sooner),
        };

        Assert.Equal(sooner, StockCalculator.EarliestExpiry(movements));
        Assert.Null(StockCalculator.EarliestExpiry([Movement(MovementKind.Sale, -1m, 1_000)]));
    }

    [Fact]
    public void Project_ProducesAFullStockLevel()
    {
        var movements = new List<StockMovement>
        {
            Movement(MovementKind.Receipt, 100m, 2_000, expiry: T0.AddDays(60)),
            Movement(MovementKind.Sale, -40m, 2_000),
        };

        var level = StockCalculator.Project("P1", "V1", movements, reserved: new Quantity(10m, "EA"));

        Assert.Equal(60m, level.OnHand.Value);
        Assert.Equal(10m, level.Reserved.Value);
        Assert.Equal(50m, level.Available.Value);
        Assert.Equal(Money.Irt(2_000), level.AverageCost);
        Assert.Equal(T0.AddDays(60), level.EarliestExpiryUtc);
    }

    [Fact]
    public void MixedUnits_AreADefectAndThrow()
    {
        var movements = new List<StockMovement>
        {
            StockMovement.Create("T1", "P1", "V1", MovementKind.Receipt, new Quantity(1m, "EA"),
                Money.Irt(1), T0, "U1").Value!,
            StockMovement.Create("T1", "P1", "V1", MovementKind.Receipt, new Quantity(1m, "ML"),
                Money.Irt(1), T0, "U1").Value!,
        };

        Assert.Throws<InvalidOperationException>(() => StockCalculator.OnHand(movements));
    }
}
