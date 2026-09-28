using Animora.Domain.Boarding;
using Animora.Domain.ValueObjects;
using Xunit;

namespace Animora.Domain.Tests;

public sealed class StayChargeCalculatorTests
{
    private static readonly DateTimeOffset Start = new(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);

    private static BoardingStay Stay() => BoardingStay.Reserve(
        "tenant", "pet", "owner", "room", Start, Start.AddDays(5), Money.Irt(100)).Value!;

    private static void Add(BoardingStay stay, BoardingServiceDef service) =>
        Assert.True(stay.AddItem(new StayItem(Guid.NewGuid().ToString(), StayItemKind.BoardingService,
            service.Id, service.Title, new Quantity(1, "PCS"), service.UnitPrice, Start, "staff")).IsSuccess);

    [Fact]
    public void SameDayCountsOneNight()
    {
        Assert.Equal(1, StayChargeCalculator.NightsBetween(Start, Start.AddHours(5), TimeZoneInfo.Utc));
        Assert.Equal(Money.Irt(100), StayChargeCalculator.Accrued(Stay(), [], Start.AddHours(5), TimeZoneInfo.Utc));
    }

    [Fact]
    public void PerDayServiceAccruesAcrossThreeLocalNights()
    {
        var stay = Stay();
        var service = BoardingServiceDef.Create("tenant", "WALK", "Walk", Money.Irt(20), BoardingServiceBilling.PerDay);
        Add(stay, service);
        Assert.Equal(Money.Irt(360), StayChargeCalculator.Accrued(stay, [service], Start.AddDays(3), TimeZoneInfo.Utc));
    }

    [Fact]
    public void PerStayServiceIsChargedOnce()
    {
        var stay = Stay();
        var service = BoardingServiceDef.Create("tenant", "BATH", "Bath", Money.Irt(20), BoardingServiceBilling.PerStay);
        Add(stay, service);
        Assert.Equal(Money.Irt(320), StayChargeCalculator.Accrued(stay, [service], Start.AddDays(3), TimeZoneInfo.Utc));
    }

    [Fact]
    public void FrozenCheckoutSnapshotMatchesRecomputationAtCheckoutInstant()
    {
        var stay = Stay();
        var clock = new FakeClock(Start.AddDays(3));
        Assert.True(stay.Activate(clock).IsSuccess);
        var charge = StayChargeCalculator.Accrued(stay, [], clock.UtcNow, TimeZoneInfo.Utc);
        Assert.True(stay.RequestCheckout(clock).IsSuccess);
        Assert.True(stay.FreezeTotalForCheckout(charge).IsSuccess);
        Assert.Equal(charge.Minor, stay.FrozenTotalMinor);
        Assert.Equal(charge, StayChargeCalculator.Accrued(stay, [], clock.UtcNow, TimeZoneInfo.Utc));
        Assert.True(stay.FreezeTotalForCheckout(charge).IsFailure);
    }
}
