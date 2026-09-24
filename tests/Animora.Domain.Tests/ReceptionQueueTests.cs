using System;
using System.Collections.Generic;
using Animora.Domain.Results;
using Animora.Domain.Scheduling;
using Xunit;

namespace Animora.Domain.Tests;

/// <summary>
/// The reception-queue state machine, the derived-entry rule (an entry exists only for a present
/// patient) and the clamped triage priority.
/// </summary>
public sealed class ReceptionQueueTests
{
    private static readonly DateTimeOffset Enqueued = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private static readonly HashSet<(QueueState From, QueueState To)> Allowed =
    [
        (QueueState.Waiting, QueueState.Called),
        (QueueState.Waiting, QueueState.Left),
        (QueueState.Called, QueueState.InVisit),
        (QueueState.Called, QueueState.Waiting),
        (QueueState.Called, QueueState.Left),
        (QueueState.InVisit, QueueState.Done),
    ];

    [Fact]
    public void AllStatePairs_MatchTheTable()
    {
        var states = Enum.GetValues<QueueState>();
        foreach (var from in states)
        {
            foreach (var to in states)
            {
                Assert.Equal(Allowed.Contains((from, to)), ReceptionQueueEntry.IsTransitionAllowed(from, to));
            }
        }
    }

    private static ReceptionQueueEntry Create(FakeClock clock, int priority = 0, string? appointmentId = null) =>
        ReceptionQueueEntry.Create("T1", "PET1", "OWN1", QueueSource.WalkIn, 1, clock,
            appointmentId: appointmentId, priority: priority);

    [Fact]
    public void Create_StartsWaitingWithClampedPriority()
    {
        var clock = new FakeClock(Enqueued);

        var emergency = Create(clock, priority: 5);
        Assert.Equal(ReceptionQueueEntry.PriorityEmergency, emergency.Priority);
        Assert.Equal(QueueState.Waiting, emergency.State);
        Assert.Equal(Enqueued, emergency.EnqueuedUtc);

        var belowFloor = Create(clock, priority: -3);
        Assert.Equal(ReceptionQueueEntry.PriorityNormal, belowFloor.Priority);
    }

    [Fact]
    public void DerivedEntry_OnlyLinksAnAppointmentWhenGiven()
    {
        var clock = new FakeClock(Enqueued);

        Assert.Null(Create(clock, appointmentId: "   ").AppointmentId);
        Assert.Equal("APPT-1", Create(clock, appointmentId: "APPT-1").AppointmentId);
    }

    [Fact]
    public void HappyPath_WaitingToDone()
    {
        var clock = new FakeClock(Enqueued);
        var entry = Create(clock);

        Assert.True(entry.Call("DOC1", clock).IsSuccess);
        Assert.Equal("DOC1", entry.DoctorUserId);
        Assert.Equal(Enqueued, entry.CalledUtc);

        Assert.True(entry.StartVisit("VISIT-1").IsSuccess);
        Assert.Equal("VISIT-1", entry.VisitId);

        Assert.True(entry.Close(clock).IsSuccess);
        Assert.Equal(QueueState.Done, entry.State);
        Assert.Equal(clock.UtcNow, entry.ClosedUtc);
    }

    [Fact]
    public void CalledCanReturnToWaiting()
    {
        var clock = new FakeClock(Enqueued);
        var entry = Create(clock);
        entry.Call("DOC1", clock);

        Assert.True(ReceptionQueueEntry.IsTransitionAllowed(QueueState.Called, QueueState.Waiting));
    }

    [Fact]
    public void Left_IsTerminalAndClosesTheEntry()
    {
        var clock = new FakeClock(Enqueued);
        var entry = Create(clock);

        Assert.True(entry.MarkLeft(clock).IsSuccess);
        Assert.Equal(QueueState.Left, entry.State);
        Assert.Equal(clock.UtcNow, entry.ClosedUtc);

        var result = entry.Call("DOC1", clock);
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.QueueInvalidTransition, result.Error);
    }

    [Fact]
    public void SetPriority_IsClampedAndRefusedOnceClosed()
    {
        var clock = new FakeClock(Enqueued);
        var entry = Create(clock);

        Assert.True(entry.SetPriority(1).IsSuccess);
        Assert.Equal(1, entry.Priority);

        entry.MarkLeft(clock);
        var result = entry.SetPriority(2);
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.QueueInvalidTransition, result.Error);
        Assert.Equal(1, entry.Priority);
    }
}
