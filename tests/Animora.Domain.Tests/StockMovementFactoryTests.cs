using System;
using System.Collections.Generic;
using Animora.Domain.Inventory;
using Animora.Domain.Results;
using Animora.Domain.ValueObjects;
using Xunit;

namespace Animora.Domain.Tests;

/// <summary>
/// The movement factory's sign rule: the four inbound kinds require a positive quantity, every
/// other kind a negative one, zero is never a movement — and a reversal negates the original and
/// links back to it.
/// </summary>
public sealed class StockMovementFactoryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    public static IEnumerable<object[]> AllKinds() =>
        Enum.GetValues<MovementKind>().Select(k => new object[] { k });

    private static Result<StockMovement> Create(MovementKind kind, decimal quantity) =>
        StockMovement.Create("T1", "P1", "V1", kind, new Quantity(quantity, "EA"),
            Money.Irt(1_000), T0, "U1");

    [Theory]
    [MemberData(nameof(AllKinds))]
    public void EveryKind_EnforcesItsRequiredSign(MovementKind kind)
    {
        var inbound = StockMovement.IsInboundKind(kind);
        var valid = inbound ? 5m : -5m;
        var invalid = -valid;

        var accepted = Create(kind, valid);
        Assert.True(accepted.IsSuccess);
        Assert.Equal(kind, accepted.Value!.Kind);
        Assert.Equal(valid, accepted.Value.Quantity.Value);
        Assert.Equal(inbound, accepted.Value.IsInbound);

        var rejected = Create(kind, invalid);
        Assert.True(rejected.IsFailure);
        Assert.Equal(ErrorCodes.MovementSignInvalid, rejected.Error);
    }

    [Theory]
    [MemberData(nameof(AllKinds))]
    public void ZeroQuantity_IsNeverAMovement(MovementKind kind)
    {
        var result = Create(kind, 0m);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.MovementSignInvalid, result.Error);
    }

    [Fact]
    public void AllTenKinds_AreCovered()
    {
        Assert.Equal(10, Enum.GetValues<MovementKind>().Length);
    }

    [Fact]
    public void OppositeKind_FlipsEverySign()
    {
        foreach (var kind in Enum.GetValues<MovementKind>())
        {
            Assert.Equal(StockMovement.IsInboundKind(kind), !StockMovement.IsInboundKind(StockMovement.OppositeKind(kind)));
        }
    }

    [Fact]
    public void Reverse_NegatesQuantityAndLinksToTheOriginal()
    {
        var original = Create(MovementKind.Receipt, 25m).Value!;

        var reversal = StockMovement.Reverse(original, T0.AddHours(1), "U2", "wrong lot").Value!;

        Assert.Equal(MovementKind.ReturnOut, reversal.Kind);
        Assert.Equal(-25m, reversal.Quantity.Value);
        Assert.Equal(original.Id, reversal.ReversesMovementId);
        Assert.True(reversal.IsReversal);
        Assert.Equal(original.LotNumber, reversal.LotNumber);
    }

    [Fact]
    public void Reverse_OfAnOutboundMovement_IsInbound()
    {
        var sale = Create(MovementKind.Sale, -10m).Value!;

        var reversal = StockMovement.Reverse(sale, T0, "U2", "client returned").Value!;

        Assert.Equal(MovementKind.ReturnIn, reversal.Kind);
        Assert.Equal(10m, reversal.Quantity.Value);
        Assert.True(reversal.IsInbound);
    }
}
