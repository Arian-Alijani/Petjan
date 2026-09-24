using System;
using System.Collections.Generic;
using Animora.Domain.Boarding;
using Animora.Domain.Results;
using Animora.Domain.ValueObjects;
using Xunit;

namespace Animora.Domain.Tests;

/// <summary>
/// The boarding-stay state machine, the authoritative billable-days rule (1h → 1, 24h → 1, 25h → 2)
/// and the "never checked out unbilled" guard.
/// </summary>
public sealed class BoardingStayTests
{
    private static readonly DateTimeOffset CheckIn = new(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);

    private static readonly HashSet<(StayState From, StayState To)> Allowed =
    [
        (StayState.Reserved, StayState.Active),
        (StayState.Reserved, StayState.Cancelled),
        (StayState.Active, StayState.ReadyForInvoice),
        (StayState.Active, StayState.Cancelled),
        (StayState.ReadyForInvoice, StayState.Invoiced),
        (StayState.Invoiced, StayState.CheckedOut),
    ];

    private static BoardingStay Reserve(FakeClock clock, double hours = 24) =>
        BoardingStay.Reserve("T1", "PET1", "OWN1", "ROOM1", CheckIn,
            CheckIn.AddHours(hours), Money.Irt(500_000)).Value!;

    [Fact]
    public void AllStatePairs_MatchTheTable()
    {
        var states = Enum.GetValues<StayState>();
        foreach (var from in states)
        {
            foreach (var to in states)
            {
                Assert.Equal(Allowed.Contains((from, to)), BoardingStay.IsTransitionAllowed(from, to));
            }
        }
    }

    [Fact]
    public void Reserve_WithCheckOutNotAfterCheckIn_IsRefused()
    {
        var result = BoardingStay.Reserve("T1", "PET1", "OWN1", "ROOM1", CheckIn, CheckIn, Money.Irt(1));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.StayDatesInvalid, result.Error);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(24, 1)]
    [InlineData(25, 2)]
    [InlineData(48, 2)]
    [InlineData(49, 3)]
    public void BillableDays_CountsPartialDaysAsFull(double hours, int expected)
    {
        var stay = Reserve(new FakeClock(CheckIn), hours);

        Assert.Equal(expected, stay.BillableDays(CheckIn.AddHours(hours)));
    }

    [Fact]
    public void Activate_OpensFirstRoomAssignment()
    {
        var clock = new FakeClock(CheckIn);
        var stay = Reserve(clock);
        Assert.True(stay.Activate(clock).IsSuccess);

        Assert.Equal(StayState.Active, stay.State);
        Assert.Single(stay.RoomHistory);
        Assert.Equal("ROOM1", stay.RoomHistory[0].RoomId);
        Assert.Null(stay.RoomHistory[0].ToUtc);
    }

    [Fact]
    public void TransferRoom_ClosesCurrentAndAppendsNewAssignment()
    {
        var clock = new FakeClock(CheckIn);
        var stay = Reserve(clock);
        stay.Activate(clock);

        clock.Advance(TimeSpan.FromHours(3));
        Assert.True(stay.TransferRoom("ROOM2", "isolation", clock).IsSuccess);

        Assert.Equal(2, stay.RoomHistory.Count);
        Assert.Equal(CheckIn.AddHours(3), stay.RoomHistory[0].ToUtc);
        Assert.Equal("ROOM2", stay.RoomHistory[1].RoomId);
        Assert.Null(stay.RoomHistory[1].ToUtc);
        Assert.Equal("ROOM2", stay.RoomId);
    }

    [Fact]
    public void TransferRoom_WhenNotActive_IsRefused()
    {
        var clock = new FakeClock(CheckIn);
        var stay = Reserve(clock);

        var result = stay.TransferRoom("ROOM2", "why", clock);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.StayInvalidTransition, result.Error);
    }

    [Fact]
    public void CompleteCheckout_WithoutInvoice_IsRefused()
    {
        var clock = new FakeClock(CheckIn);
        var stay = Reserve(clock);
        stay.Activate(clock);
        stay.RequestCheckout(clock);

        var result = stay.CompleteCheckout(clock);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.StayInvoiceRequired, result.Error);
        Assert.Equal(StayState.ReadyForInvoice, stay.State);
        Assert.Null(stay.ActualCheckOutUtc);
    }

    [Fact]
    public void HappyPath_ReserveToCheckedOut()
    {
        var clock = new FakeClock(CheckIn);
        var stay = Reserve(clock);

        Assert.True(stay.Activate(clock).IsSuccess);
        Assert.True(stay.RequestCheckout(clock).IsSuccess);
        Assert.True(stay.AttachInvoice("INV-1").IsSuccess);
        Assert.Equal(StayState.Invoiced, stay.State);
        Assert.True(stay.CompleteCheckout(clock).IsSuccess);
        Assert.Equal(StayState.CheckedOut, stay.State);
        Assert.Equal(clock.UtcNow, stay.ActualCheckOutUtc);
        Assert.NotNull(stay.RoomHistory[0].ToUtc);
    }

    [Fact]
    public void Items_AreLockedOnceInvoiced()
    {
        var clock = new FakeClock(CheckIn);
        var stay = Reserve(clock);
        stay.Activate(clock);
        stay.RequestCheckout(clock);
        stay.AttachInvoice("INV-1");

        var item = new StayItem("I1", StayItemKind.ClinicService, null, "Bath",
            new Quantity(1m, "PCS"), Money.Irt(50_000), CheckIn, "U1");
        var result = stay.AddItem(item);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.StayLocked, result.Error);
    }
}
