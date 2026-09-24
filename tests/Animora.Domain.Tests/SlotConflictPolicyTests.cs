using System;
using System.Collections.Generic;
using Animora.Domain.Results;
using Animora.Domain.Scheduling;
using Xunit;

namespace Animora.Domain.Tests;

/// <summary>
/// The authoritative slot-conflict rule (ARCHITECTURE.md §8.5): only a same-doctor overlap of a
/// non-terminal appointment conflicts; adjacent slots never do; an unassigned candidate never does.
/// </summary>
public sealed class SlotConflictPolicyTests
{
    private static readonly DateTimeOffset Start = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private static AppointmentSlot Slot(int startMinutes, int durationMinutes) =>
        new(Start.AddMinutes(startMinutes), durationMinutes);

    private static ExistingSlot Existing(int startMinutes, int durationMinutes, string doctor = "DOC1",
        AppointmentState state = AppointmentState.Scheduled, string id = "APPT-1") =>
        new(id, doctor, Slot(startMinutes, durationMinutes), state);

    [Fact]
    public void ExactOverlap_Conflicts()
    {
        var result = SlotConflictPolicy.EnsureFree(Slot(0, 30), "DOC1", [Existing(0, 30)]);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.AppointmentSlotConflict, result.Error);
        Assert.Equal("APPT-1", result.Detail);
    }

    [Fact]
    public void PartialOverlap_Conflicts()
    {
        var result = SlotConflictPolicy.EnsureFree(Slot(15, 30), "DOC1", [Existing(0, 30)]);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.AppointmentSlotConflict, result.Error);
    }

    [Fact]
    public void AdjacentSlots_DoNotConflict()
    {
        // end == start on both sides: back-to-back appointments are legal (half-open interval).
        Assert.True(SlotConflictPolicy.EnsureFree(Slot(30, 30), "DOC1", [Existing(0, 30)]).IsSuccess);
        Assert.True(SlotConflictPolicy.EnsureFree(Slot(0, 30), "DOC1", [Existing(30, 30)]).IsSuccess);
    }

    [Fact]
    public void DifferentDoctor_DoesNotConflict()
    {
        var result = SlotConflictPolicy.EnsureFree(Slot(0, 30), "DOC1", [Existing(0, 30, doctor: "DOC2")]);

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData(AppointmentState.Cancelled)]
    [InlineData(AppointmentState.NoShow)]
    [InlineData(AppointmentState.Completed)]
    public void TerminalExistingSlot_DoesNotConflict(AppointmentState state)
    {
        var result = SlotConflictPolicy.EnsureFree(Slot(0, 30), "DOC1", [Existing(0, 30, state: state)]);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void NullDoctorCandidate_DoesNotConflict()
    {
        var result = SlotConflictPolicy.EnsureFree(Slot(0, 30), null, [Existing(0, 30)]);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void FirstBlockingOverlap_IsReturned()
    {
        var existing = new List<ExistingSlot>
        {
            Existing(0, 30, doctor: "DOC2", id: "OTHER-DOCTOR"),
            Existing(0, 30, doctor: "DOC1", id: "BLOCKER"),
            Existing(0, 30, doctor: "DOC1", id: "LATER"),
        };

        var result = SlotConflictPolicy.EnsureFree(Slot(0, 30), "DOC1", existing);

        Assert.True(result.IsFailure);
        Assert.Equal("BLOCKER", result.Detail);
    }

    [Fact]
    public void EmptyCalendar_IsFree()
    {
        Assert.True(SlotConflictPolicy.EnsureFree(Slot(0, 30), "DOC1", []).IsSuccess);
    }
}
