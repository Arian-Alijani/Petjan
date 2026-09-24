using System;
using System.Collections.Generic;
using Animora.Domain.Results;
using Animora.Domain.Scheduling;
using Xunit;

namespace Animora.Domain.Tests;

/// <summary>
/// The appointment state machine: exactly the tabled transitions are legal and the rest are refused
/// with <c>APPOINTMENT_INVALID_TRANSITION</c>; duration must be a 5-minute multiple in [5, 480];
/// a slot is fixed from <see cref="AppointmentState.InVisit"/> on.
/// </summary>
public sealed class AppointmentStateMachineTests
{
    private static readonly DateTimeOffset Start = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private static readonly HashSet<(AppointmentState From, AppointmentState To)> Allowed =
    [
        (AppointmentState.Scheduled, AppointmentState.Confirmed),
        (AppointmentState.Scheduled, AppointmentState.CheckedIn),
        (AppointmentState.Scheduled, AppointmentState.Cancelled),
        (AppointmentState.Scheduled, AppointmentState.NoShow),
        (AppointmentState.Confirmed, AppointmentState.CheckedIn),
        (AppointmentState.Confirmed, AppointmentState.Cancelled),
        (AppointmentState.Confirmed, AppointmentState.NoShow),
        (AppointmentState.CheckedIn, AppointmentState.InVisit),
        (AppointmentState.CheckedIn, AppointmentState.Cancelled),
        (AppointmentState.CheckedIn, AppointmentState.NoShow),
        (AppointmentState.InVisit, AppointmentState.Completed),
        (AppointmentState.InVisit, AppointmentState.Cancelled),
    ];

    [Fact]
    public void AllStatePairs_MatchTheTable()
    {
        var states = Enum.GetValues<AppointmentState>();
        Assert.Equal(49, states.Length * states.Length);

        foreach (var from in states)
        {
            foreach (var to in states)
            {
                Assert.Equal(Allowed.Contains((from, to)), Appointment.IsTransitionAllowed(from, to));
            }
        }
    }

    private static Appointment Booked(FakeClock clock, int durationMinutes = 30) =>
        Appointment.Book("T1", "PET1", "OWN1", "CHECKUP", Start, durationMinutes,
            AppointmentSource.Phone, "U1", clock, doctorUserId: "DOC1").Value!;

    [Fact]
    public void HappyPath_ScheduledToCompleted()
    {
        var clock = new FakeClock(Start);
        var appointment = Booked(clock);

        Assert.True(appointment.Confirm().IsSuccess);
        Assert.True(appointment.CheckIn(clock).IsSuccess);
        Assert.Equal(clock.UtcNow, appointment.CheckedInUtc);
        Assert.True(appointment.StartVisit("VISIT-1").IsSuccess);
        Assert.Equal("VISIT-1", appointment.VisitId);
        Assert.True(appointment.Complete().IsSuccess);
        Assert.Equal(AppointmentState.Completed, appointment.State);
    }

    [Fact]
    public void Book_WithSevenMinuteDuration_IsRefused()
    {
        var result = Appointment.Book("T1", "PET1", "OWN1", "CHECKUP", Start, 7,
            AppointmentSource.Phone, "U1", new FakeClock(Start), doctorUserId: "DOC1");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.AppointmentDurationInvalid, result.Error);
    }

    [Fact]
    public void Move_WithSevenMinuteDuration_IsRefused()
    {
        var clock = new FakeClock(Start);
        var appointment = Booked(clock);

        var result = appointment.Move(Start.AddHours(1), 7, clock);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.AppointmentDurationInvalid, result.Error);
        Assert.Equal(0, appointment.RescheduleCount);
    }

    [Fact]
    public void Move_BeforeVisit_SucceedsAndCounts()
    {
        var clock = new FakeClock(Start);
        var appointment = Booked(clock);

        var result = appointment.Move(Start.AddHours(2), 45, clock);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, appointment.RescheduleCount);
        Assert.Equal(Start.AddHours(2), appointment.StartUtc);
        Assert.Equal(45, appointment.DurationMinutes);
        Assert.Equal(Start.AddHours(2).AddMinutes(45), appointment.EndUtc);
    }

    [Fact]
    public void Move_AfterVisitStarted_IsRefused()
    {
        var clock = new FakeClock(Start);
        var appointment = Booked(clock);
        appointment.Confirm();
        appointment.CheckIn(clock);
        appointment.StartVisit("VISIT-1");

        var result = appointment.Move(Start.AddHours(3), 30, clock);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.AppointmentNotMovable, result.Error);
        Assert.Equal(0, appointment.RescheduleCount);
    }

    [Fact]
    public void TerminalStates_RefuseEveryFurtherTransition()
    {
        var clock = new FakeClock(Start);
        var cancelled = Booked(clock);
        Assert.True(cancelled.Cancel("owner called", clock).IsSuccess);
        Assert.Equal("owner called", cancelled.CancelReason);
        AssertInvalid(cancelled.Confirm());

        var completed = Booked(clock);
        completed.Confirm();
        completed.CheckIn(clock);
        completed.StartVisit("V");
        completed.Complete();
        AssertInvalid(completed.Cancel("too late", clock));
    }

    [Fact]
    public void CheckIn_IsRefusedFromTerminalState_AndDoesNotStamp()
    {
        var clock = new FakeClock(Start);
        var appointment = Booked(clock);
        appointment.Cancel("no show", clock);

        var result = appointment.CheckIn(clock);

        Assert.True(result.IsFailure);
        Assert.Null(appointment.CheckedInUtc);
    }

    private static void AssertInvalid(Result result)
    {
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.AppointmentInvalidTransition, result.Error);
    }
}
