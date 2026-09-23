using System;
using System.Collections.Generic;
using Animora.Domain.Clinical;
using Animora.Domain.Events;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Xunit;

namespace Animora.Domain.Tests;

/// <summary>
/// The encounter state machine: exactly the 6 tabled transitions are legal and the other 30 of the
/// 36 <see cref="VisitState"/> pairs are refused with <c>VISIT_INVALID_TRANSITION</c>.
/// No test reads the wall clock (CONVENTIONS.md §9).
/// </summary>
public sealed class VisitStateMachineTests
{
    private static readonly DateTimeOffset Start = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private static readonly HashSet<(VisitState From, VisitState To)> Allowed =
    [
        (VisitState.Draft, VisitState.InProgress),
        (VisitState.Draft, VisitState.Cancelled),
        (VisitState.InProgress, VisitState.Completed),
        (VisitState.InProgress, VisitState.Cancelled),
        (VisitState.Completed, VisitState.ReadyForInvoice),
        (VisitState.ReadyForInvoice, VisitState.Invoiced),
    ];

    [Fact]
    public void AllStatePairs_MatchTheTable()
    {
        var states = Enum.GetValues<VisitState>();
        Assert.Equal(36, states.Length * states.Length);

        foreach (var from in states)
        {
            foreach (var to in states)
            {
                Assert.Equal(Allowed.Contains((from, to)), Visit.IsTransitionAllowed(from, to));
            }
        }
    }

    [Fact]
    public void HappyPath_DraftToInvoiced_SucceedsAndRaisesBothEvents()
    {
        var clock = new TestClock(Start);
        var visit = OpenVisit(clock);

        Assert.True(visit.Start().IsSuccess);
        Assert.Equal(VisitState.InProgress, visit.State);

        Assert.True(visit.SetAssessment("Otitis externa").IsSuccess);
        Assert.True(visit.Complete(clock).IsSuccess);
        Assert.Equal(VisitState.Completed, visit.State);
        Assert.Equal(clock.UtcNow, visit.CompletedUtc);

        Assert.True(visit.MarkReadyForInvoice().IsSuccess);
        Assert.Equal(VisitState.ReadyForInvoice, visit.State);

        Assert.True(visit.AttachInvoice("INV-1").IsSuccess);
        Assert.Equal(VisitState.Invoiced, visit.State);
        Assert.Equal("INV-1", visit.InvoiceId);

        Assert.Collection(
            visit.DomainEvents,
            e => Assert.Equal(visit.Id, Assert.IsType<VisitCompletedEvent>(e).VisitId),
            e => Assert.Equal(visit.Id, Assert.IsType<VisitReadyForInvoiceEvent>(e).VisitId));
    }

    [Theory]
    [InlineData(VisitState.Draft)]
    [InlineData(VisitState.InProgress)]
    public void Cancel_IsLegalBeforeCompletion(VisitState from)
    {
        var clock = new TestClock(Start);
        var visit = OpenVisit(clock);
        if (from == VisitState.InProgress)
        {
            Assert.True(visit.Start().IsSuccess);
        }

        Assert.True(visit.Cancel("owner left", clock).IsSuccess);
        Assert.Equal(VisitState.Cancelled, visit.State);
        Assert.Equal("owner left", visit.CancellationReason);
    }

    [Fact]
    public void TerminalStates_RefuseEveryFurtherTransition()
    {
        var clock = new TestClock(Start);

        var cancelled = OpenVisit(clock);
        Assert.True(cancelled.Cancel("no show", clock).IsSuccess);
        AssertInvalid(cancelled.Start());
        AssertInvalid(cancelled.MarkReadyForInvoice());
        AssertInvalid(cancelled.AttachInvoice("INV-2"));

        var invoiced = InvoicedVisit(clock);
        AssertInvalid(invoiced.Start());
        AssertInvalid(invoiced.MarkReadyForInvoice());
        AssertInvalid(invoiced.Cancel("too late", clock));
        AssertInvalid(invoiced.AttachInvoice("INV-3"));
    }

    [Fact]
    public void AttachInvoice_BeforeHandoff_IsRefused()
    {
        var clock = new TestClock(Start);
        var visit = OpenVisit(clock);
        Assert.True(visit.Start().IsSuccess);
        Assert.True(visit.SetAssessment("Gastritis").IsSuccess);
        Assert.True(visit.Complete(clock).IsSuccess);

        AssertInvalid(visit.AttachInvoice("INV-4"));
        Assert.Null(visit.InvoiceId);
        Assert.Equal(VisitState.Completed, visit.State);
    }

    internal static Visit OpenVisit(IClock clock) =>
        Visit.Open("T1", "PET1", "OWN1", "DOC1", VisitReason.Illness, clock);

    internal static Visit InvoicedVisit(IClock clock)
    {
        var visit = OpenVisit(clock);
        visit.Start();
        visit.SetAssessment("Dermatitis");
        visit.Complete(clock);
        visit.MarkReadyForInvoice();
        visit.AttachInvoice("INV-0");
        return visit;
    }

    private static void AssertInvalid(Result result)
    {
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.VisitInvalidTransition, result.Error);
    }

    internal sealed class TestClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = now;

        public void Advance(TimeSpan by) => UtcNow += by;
    }
}
