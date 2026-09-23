using System;
using Animora.Domain.Clinical;
using Animora.Domain.Results;
using Animora.Domain.ValueObjects;
using Xunit;
using TestClock = Animora.Domain.Tests.VisitStateMachineTests.TestClock;

namespace Animora.Domain.Tests;

/// <summary>
/// Encounter invariants that survive every state change: a diagnosis is mandatory before
/// completion, and the billable/prescription lines freeze once Sales has invoiced them.
/// </summary>
public sealed class VisitInvariantTests
{
    private static readonly DateTimeOffset Start = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Complete_WithoutDiagnosis_IsRefused()
    {
        var clock = new TestClock(Start);
        var visit = VisitStateMachineTests.OpenVisit(clock);
        visit.Start();

        var blank = visit.Complete(clock);
        Assert.True(blank.IsFailure);
        Assert.Equal(ErrorCodes.VisitDiagnosisRequired, blank.Error);
        Assert.Equal(VisitState.InProgress, visit.State);
        Assert.Empty(visit.DomainEvents);

        Assert.True(visit.SetAssessment("   ").IsSuccess);
        Assert.True(visit.Complete(clock).IsFailure);

        Assert.True(visit.SetAssessment("Pyoderma").IsSuccess);
        Assert.True(visit.Complete(clock).IsSuccess);
    }

    [Fact]
    public void LinesAreLocked_AfterInvoicing()
    {
        var clock = new TestClock(Start);
        var visit = VisitStateMachineTests.InvoicedVisit(clock);

        Assert.True(visit.AreLinesLocked);
        AssertLocked(visit.AddServiceLine(ServiceLine("L9")));
        AssertLocked(visit.AddPrescription(Prescription("P9")));
        AssertLocked(visit.RemoveServiceLine("L1"));
        AssertLocked(visit.RemovePrescription("P1"));
    }

    [Fact]
    public void Lines_AreEditableUntilInvoiced_AndExposedReadOnly()
    {
        var clock = new TestClock(Start);
        var visit = VisitStateMachineTests.OpenVisit(clock);
        visit.Start();

        Assert.True(visit.AddServiceLine(ServiceLine("L1")).IsSuccess);
        Assert.True(visit.AddServiceLine(ServiceLine("L2")).IsSuccess);
        Assert.True(visit.AddPrescription(Prescription("P1")).IsSuccess);
        Assert.Equal(2, visit.ServiceLines.Count);
        Assert.Single(visit.Prescriptions);

        Assert.True(visit.RemoveServiceLine("L1").IsSuccess);
        Assert.Single(visit.ServiceLines);

        var missing = visit.RemoveServiceLine("L1");
        Assert.True(missing.IsFailure);
        Assert.Equal(ErrorCodes.NotFound, missing.Error);

        // Still editable after completion and handoff — Sales may correct lines until it invoices.
        visit.SetAssessment("Abscess");
        visit.Complete(clock);
        visit.MarkReadyForInvoice();
        Assert.True(visit.AddServiceLine(ServiceLine("L3")).IsSuccess);
    }

    [Fact]
    public void ClinicalData_IsClosedOnceTheEncounterEnds()
    {
        var clock = new TestClock(Start);
        var visit = VisitStateMachineTests.OpenVisit(clock);
        visit.Start();
        visit.SetAssessment("Conjunctivitis");
        visit.Complete(clock);

        var vitals = visit.SetVitals(new PetVitals(12.5m, 38.6m, 96, 22, null, null));
        Assert.True(vitals.IsFailure);
        Assert.Equal(ErrorCodes.VisitInvalidTransition, vitals.Error);
        Assert.Null(visit.Vitals);

        Assert.True(visit.SetAssessment("rewritten").IsFailure);
        Assert.Equal("Conjunctivitis", visit.Diagnosis);
    }

    private static void AssertLocked(Result result)
    {
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.VisitLocked, result.Error);
    }

    private static VisitServiceLine ServiceLine(string lineId) =>
        new(lineId, "SVC1", "Consultation", new Quantity(1m, "EA"), Money.Irt(500_000), VisitLineSource.Service);

    private static PrescriptionLine Prescription(string lineId) =>
        new(lineId, null, "Amoxicillin", "250 mg", "BID", 7, null);
}
