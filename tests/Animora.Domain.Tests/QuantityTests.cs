using System;
using Animora.Domain.ValueObjects;
using Xunit;

namespace Animora.Domain.Tests;

/// <summary>Quantity math is unit-safe; signed values stay legal (movements may be negative).</summary>
public sealed class QuantityTests
{
    [Fact]
    public void AddSubtract_KeepUnitAndAllowNegative()
    {
        var two = new Quantity(2m, "EA");
        var five = new Quantity(5m, "EA");

        Assert.Equal(new Quantity(7m, "EA"), two + five);
        Assert.Equal(new Quantity(-3m, "EA"), two - five);
        Assert.Equal(new Quantity(3m, "EA"), (two - five).Abs());
        Assert.True((two - five).IsNegative);
        Assert.True(Quantity.Zero("ML").IsZero);
        Assert.Equal(new Quantity(-2m, "EA"), -two);
    }

    [Fact]
    public void UnitMismatch_Throws()
    {
        var pieces = new Quantity(1m, "EA");
        var millilitres = new Quantity(1m, "ML");

        Assert.Throws<InvalidOperationException>(() => pieces + millilitres);
        Assert.Throws<InvalidOperationException>(() => pieces - millilitres);
    }
}
