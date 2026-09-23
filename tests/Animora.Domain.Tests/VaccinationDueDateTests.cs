using System;
using Animora.Domain.Clinical;
using Animora.Domain.Events;
using Animora.Domain.Results;
using Xunit;
using TestClock = Animora.Domain.Tests.VisitStateMachineTests.TestClock;

namespace Animora.Domain.Tests;

/// <summary>
/// The authoritative due-date rule: <c>NextDueUtc = AdministeredUtc + IntervalFor(code, dose)</c>,
/// the series-ending dose falls back to the annual booster, and <see cref="VaccinationRecord.StateAt"/>
/// is derived at read time rather than stored.
/// </summary>
public sealed class VaccinationDueDateTests
{
    private static readonly DateTimeOffset Given = new(2026, 2, 1, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("DHPP", 1, 21)]
    [InlineData("DHPP", 2, 21)]
    [InlineData("DHPP", 3, 365)]
    [InlineData("FVRCP", 1, 21)]
    [InlineData("FVRCP", 3, 365)]
    [InlineData("FELV", 1, 21)]
    [InlineData("FELV", 2, 365)]
    [InlineData("LEPTO", 1, 21)]
    [InlineData("LEPTO", 2, 365)]
    [InlineData("RABIES", 1, 365)]
    [InlineData("BORDETELLA", 1, 365)]
    public void SeededCodes_UsePerDoseIntervals(string code, int dose, int expectedDays)
    {
        Assert.Equal(expectedDays, VaccineSchedule.IntervalFor(code, dose));

        var record = Administer(code, dose);
        Assert.Equal(Given.AddDays(expectedDays), record.NextDueUtc);
    }

    [Theory]
    [InlineData("DHPP", 9)] // beyond the series → clamps to the last entry (the booster)
    [InlineData("UNKNOWN-VAX", 1)] // unknown code → annual booster, never "no due date"
    public void ClampAndUnknownCode_FallBackToTheAnnualBooster(string code, int dose)
    {
        Assert.Equal(VaccineSchedule.AnnualBoosterDays, VaccineSchedule.IntervalFor(code, dose));
    }

    [Fact]
    public void Administer_RaisesTheEventWithTheDerivedDueDate()
    {
        var record = Administer("RABIES", 1);

        var e = Assert.IsType<VaccinationAdministeredEvent>(Assert.Single(record.DomainEvents));
        Assert.Equal(record.Id, e.RecordId);
        Assert.Equal("RABIES", e.VaccineCode);
        Assert.Equal(record.NextDueUtc, e.NextDueUtc);
        Assert.Equal(1, record.TotalDoses);
    }

    [Fact]
    public void StateAt_BoundariesAreExactToTheSecond()
    {
        var record = Administer("DHPP", 1);
        var due = record.NextDueUtc!.Value;

        Assert.Equal(VaccinationState.Overdue, record.StateAt(due.AddSeconds(1)));
        Assert.Equal(VaccinationState.DueSoon, record.StateAt(due));
        Assert.Equal(VaccinationState.DueSoon, record.StateAt(due - VaccinationRecord.DueSoonWindow));
        Assert.Equal(
            VaccinationState.Administered,
            record.StateAt(due - VaccinationRecord.DueSoonWindow - TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Skip_StopsProducingReminders()
    {
        var record = Administer("DHPP", 1);
        record.Skip("owner declined");

        Assert.Null(record.NextDueUtc);
        Assert.Equal(VaccinationState.Skipped, record.State);
        Assert.Equal(VaccinationState.Skipped, record.StateAt(Given.AddYears(5)));
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(-1, null)]
    [InlineData(4, 3)]
    public void InvalidDoseNumber_IsRefused(int dose, int? totalDoses)
    {
        var result = VaccinationRecord.Administer(
            "T1", "PET1", "DHPP", "Nobivac DHPP", dose, Given, "DOC1", new TestClock(Given), totalDoses);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.VaccineDoseInvalid, result.Error);
    }

    private static VaccinationRecord Administer(string code, int dose)
    {
        var result = VaccinationRecord.Administer(
            "T1", "PET1", code, $"{code} vaccine", dose, Given, "DOC1", new TestClock(Given));

        Assert.True(result.IsSuccess);
        return result.Value!;
    }
}
