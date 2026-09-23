using System;
using System.Linq;
using Animora.Domain.ValueObjects;
using Xunit;

namespace Animora.Domain.Tests;

/// <summary>
/// Money is integer minor units (STACK.md §3.3 / R-B24): arithmetic must be exact,
/// currency mismatch must be a defect, allocation must conserve every minor unit and
/// percent rounding must follow the single product-wide policy.
/// </summary>
public sealed class MoneyTests
{
    [Fact]
    public void Arithmetic_IsExactOnMinorUnits()
    {
        var a = Money.Irt(1_500);
        var b = Money.Irt(250);

        Assert.Equal(Money.Irt(1_750), a + b);
        Assert.Equal(Money.Irt(1_250), a - b);
        Assert.Equal(Money.Irt(-1_500), -a);
        Assert.Equal(Money.Irt(4_500), a * 3);
        Assert.Equal(Money.Irt(4_500), 3L * a);
        Assert.True(b < a);
        Assert.True(a >= Money.Irt(1_500));
        Assert.Equal(0, a.CompareTo(Money.Irt(1_500)));
        Assert.True(Money.ZeroIrt.IsZero);
        Assert.Equal(0, Money.ZeroIrt.Scale); // IRT has scale 0.
    }

    [Fact]
    public void CurrencyMismatch_Throws()
    {
        var irt = Money.Irt(100);
        var usd = Money.Of(100, "USD");

        Assert.Throws<InvalidOperationException>(() => irt + usd);
        Assert.Throws<InvalidOperationException>(() => irt - usd);
        Assert.Throws<InvalidOperationException>(() => irt.CompareTo(usd));
        Assert.Throws<ArgumentException>(() => Money.Of(1, "irt"));
    }

    [Theory]
    [InlineData(100, 3)]
    [InlineData(101, 4)]
    [InlineData(7, 7)]
    [InlineData(-100, 3)]
    public void Allocate_ConservesEveryMinorUnit(long minor, int parts)
    {
        var shares = Money.Irt(minor).Allocate(parts);

        Assert.Equal(parts, shares.Count);
        Assert.Equal(minor, shares.Sum(s => s.Minor));
        Assert.True(shares.Max(s => s.Minor) - shares.Min(s => s.Minor) <= 1);
    }

    [Fact]
    public void Allocate_PutsRemainderOnTheFirstShares()
    {
        var shares = Money.Irt(100).Allocate(3);

        Assert.Equal([34L, 33L, 33L], shares.Select(s => s.Minor));
        Assert.Throws<ArgumentOutOfRangeException>(() => Money.Irt(100).Allocate(0));
    }

    [Theory]
    [InlineData(1_000, 9, 90)]
    [InlineData(105, 50, 53)]   // 52.5 -> away from zero -> 53
    [InlineData(-105, 50, -53)] // -52.5 -> away from zero -> -53
    [InlineData(1_000, 0, 0)]
    public void ApplyPercent_RoundsAwayFromZero(long minor, decimal percent, long expected)
        => Assert.Equal(Money.Irt(expected), Money.Irt(minor).ApplyPercent(percent));
}
