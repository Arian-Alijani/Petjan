using Animora.Application.Validation;
using Animora.Domain.Primitives;
using Animora.Domain.ValueObjects;
using Xunit;

namespace Animora.Application.Tests;

public sealed class ValidationRulesTests
{
    private enum Color { Red = 1, Blue = 2 }

    private static readonly DateTimeOffset Now = new(2026, 9, 24, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Required()
    {
        Assert.Null(Rules.Required("a", "f"));
        Assert.Equal(new ValidationFailure("f", Rules.RequiredCode), Rules.Required(" ", "f"));
        Assert.NotNull(Rules.Required(null, "f"));
    }

    [Fact]
    public void MaxLength()
    {
        Assert.Null(Rules.MaxLength("abc", 3, "f"));
        Assert.Null(Rules.MaxLength(null, 3, "f"));
        Assert.Equal(Rules.MaxLengthCode, Rules.MaxLength("abcd", 3, "f")!.Code);
    }

    [Fact]
    public void Range()
    {
        Assert.Null(Rules.Range(5, 1, 10, "f"));
        Assert.Null(Rules.Range(10, 1, 10, "f"));
        Assert.Equal(Rules.RangeCode, Rules.Range(11, 1, 10, "f")!.Code);
        Assert.NotNull(Rules.Range(0m, 1m, 10m, "f"));
    }

    [Fact]
    public void PositiveMoney()
    {
        Assert.Null(Rules.PositiveMoney(new Money(1, "IRT"), "f"));
        Assert.Equal(Rules.PositiveMoneyCode, Rules.PositiveMoney(new Money(0, "IRT"), "f")!.Code);
        Assert.NotNull(Rules.PositiveMoney(new Money(-5, "IRT"), "f"));
    }

    [Fact]
    public void NonNegativeQuantity()
    {
        Assert.Null(Rules.NonNegativeQuantity(new Quantity(0m, "EA"), "f"));
        Assert.Null(Rules.NonNegativeQuantity(new Quantity(2.5m, "EA"), "f"));
        Assert.Equal(Rules.NonNegativeQuantityCode, Rules.NonNegativeQuantity(new Quantity(-1m, "EA"), "f")!.Code);
    }

    [Fact]
    public void UlidFormat()
    {
        Assert.Null(Rules.UlidFormat(IdGenerator.NewId(), "f"));
        Assert.NotNull(Rules.UlidFormat(null, "f"));
        Assert.NotNull(Rules.UlidFormat("01ARZ3NDEKTSV4RRFFQ69G5FA", "f"));   // 25 chars
        Assert.NotNull(Rules.UlidFormat("01ARZ3NDEKTSV4RRFFQ69G5FAI", "f"));  // excluded letter I
        Assert.NotNull(Rules.UlidFormat("01arz3ndektsv4rrffq69g5fav", "f"));  // lower case
        Assert.Equal(Rules.UlidFormatCode, Rules.UlidFormat("81ARZ3NDEKTSV4RRFFQ69G5FAV", "f")!.Code); // overflow
    }

    [Fact]
    public void NotFutureUtc()
    {
        Assert.Null(Rules.NotFutureUtc(Now, Now, "f"));
        Assert.Null(Rules.NotFutureUtc(Now.AddMinutes(-1), Now, "f"));
        Assert.Equal(Rules.NotFutureUtcCode, Rules.NotFutureUtc(Now.AddSeconds(1), Now, "f")!.Code);
    }

    [Fact]
    public void ValidEnum()
    {
        Assert.Null(Rules.ValidEnum(Color.Blue, "f"));
        Assert.Equal(Rules.ValidEnumCode, Rules.ValidEnum((Color)7, "f")!.Code);
    }

    [Fact]
    public void FailureDetail_RoundTrips()
    {
        ValidationFailure[] failures = [new("A", "REQUIRED"), new("B.C", "MAX_LENGTH")];
        var detail = "[corr:X] " + ValidationFailure.FormatDetail(failures);
        Assert.Equal(failures, ValidationFailure.ParseDetail(detail));
    }
}
