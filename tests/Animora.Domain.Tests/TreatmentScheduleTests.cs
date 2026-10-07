using System;
using System.Linq;
using Animora.Domain.Clinical;
using Animora.Domain.Results;
using Xunit;

namespace Animora.Domain.Tests;

/// <summary>Schedule expansion, caps and the plan/occurrence lifecycle of the «دارو و واکسن» feature.</summary>
public sealed class TreatmentScheduleTests
{
    private static readonly DateOnly Today = new(2026, 10, 7);
    private static readonly FakeClock Clock = new(new DateTimeOffset(2026, 10, 7, 8, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Once_ProducesTheSingleDay()
    {
        var r = TreatmentScheduleExpander.Expand(TreatmentSchedule.OnceOn(Today.AddDays(3)), Today);
        Assert.True(r.IsSuccess);
        Assert.Equal([Today.AddDays(3)], r.Value);
    }

    [Fact]
    public void EveryNDays_WithEnd_IsInclusive()
    {
        var r = TreatmentScheduleExpander.Expand(TreatmentSchedule.Every(Today, 2, end: Today.AddDays(6)), Today);
        Assert.Equal([Today, Today.AddDays(2), Today.AddDays(4), Today.AddDays(6)], r.Value);
    }

    [Fact]
    public void EveryNDays_WithCount_StopsAtCount()
    {
        var r = TreatmentScheduleExpander.Expand(TreatmentSchedule.Every(Today, 3, count: 4), Today);
        Assert.Equal(4, r.Value!.Count);
        Assert.Equal(Today.AddDays(9), r.Value[^1]);
    }

    [Fact]
    public void EveryNDays_NeedsEndOrCount_AndPositiveInterval()
    {
        Assert.Equal(ErrorCodes.TreatmentScheduleInvalid,
            TreatmentScheduleExpander.Expand(TreatmentSchedule.Every(Today, 2), Today).Error);
        Assert.Equal(ErrorCodes.TreatmentScheduleInvalid,
            TreatmentScheduleExpander.Expand(TreatmentSchedule.Every(Today, 0, count: 3), Today).Error);
    }

    [Fact]
    public void SpecificDates_AreDistinctAndSorted()
    {
        var d = Today.AddDays(5);
        var r = TreatmentScheduleExpander.Expand(TreatmentSchedule.OnDates([d, Today, d]), Today);
        Assert.Equal([Today, d], r.Value);
    }

    [Fact]
    public void Weekly_ReturnsOnlyChosenWeekdays()
    {
        // 2026-10-07 is a Wednesday.
        var mask = TreatmentSchedule.Bit(DayOfWeek.Wednesday) | TreatmentSchedule.Bit(DayOfWeek.Saturday);
        var r = TreatmentScheduleExpander.Expand(TreatmentSchedule.Weekly(Today, Today.AddDays(13), mask), Today);
        Assert.Equal(4, r.Value!.Count);
        Assert.All(r.Value, d => Assert.Contains(d.DayOfWeek, new[] { DayOfWeek.Wednesday, DayOfWeek.Saturday }));
    }

    [Fact]
    public void Weekly_WithoutWeekday_IsInvalid() =>
        Assert.Equal(ErrorCodes.TreatmentScheduleInvalid,
            TreatmentScheduleExpander.Expand(TreatmentSchedule.Weekly(Today, Today.AddDays(7), 0), Today).Error);

    [Fact]
    public void Oversized_IsRefused_NotTruncated()
    {
        var daily = TreatmentScheduleExpander.Expand(TreatmentSchedule.Every(Today, 1, end: Today.AddDays(400)), Today);
        Assert.Equal(ErrorCodes.TreatmentScheduleTooLarge, daily.Error);
        var picked = Enumerable.Range(0, 366).Select(i => Today.AddDays(i)).ToArray();
        Assert.Equal(ErrorCodes.TreatmentScheduleTooLarge,
            TreatmentScheduleExpander.Expand(TreatmentSchedule.OnDates(picked), Today).Error);
    }

    [Fact]
    public void ExactlyOneYearOfDailyDoses_Fits()
    {
        var r = TreatmentScheduleExpander.Expand(TreatmentSchedule.Every(Today, 1, count: 365), Today);
        Assert.True(r.IsSuccess);
        Assert.Equal(365, r.Value!.Count);
    }

    [Fact]
    public void PastBeyondWindow_AndFarFuture_AreRefused()
    {
        Assert.True(TreatmentScheduleExpander.Expand(TreatmentSchedule.OnceOn(Today.AddDays(-30)), Today).IsSuccess);
        Assert.Equal(ErrorCodes.TreatmentScheduleInvalid,
            TreatmentScheduleExpander.Expand(TreatmentSchedule.OnceOn(Today.AddDays(-31)), Today).Error);
        Assert.Equal(ErrorCodes.TreatmentScheduleInvalid,
            TreatmentScheduleExpander.Expand(TreatmentSchedule.OnceOn(Today.AddDays(366)), Today).Error);
    }

    [Fact]
    public void Plan_Create_ProducesOrderedOccurrences_AndOwnerNotifyOnlyForHome()
    {
        var home = TreatmentPlan.Create("t1", "pet1", TreatmentItemType.Medication, TreatmentKind.HomeByOwner,
            "آموکسی‌سیلین", TreatmentSchedule.Every(Today, 2, count: 3), Today, "u1", Clock, notifyOwner: true);
        Assert.True(home.IsSuccess);
        Assert.True(home.Value!.Plan.NotifyOwner);
        Assert.Equal([0, 1, 2], home.Value.Occurrences.Select(o => o.SortOrder));
        Assert.All(home.Value.Occurrences, o => Assert.Equal(home.Value.Plan.Id, o.PlanId));

        var clinic = TreatmentPlan.Create("t1", "pet1", TreatmentItemType.Vaccine, TreatmentKind.ClinicVisit,
            "DHPP", TreatmentSchedule.OnceOn(Today), Today, "u1", Clock, vaccineCode: "dhpp", notifyOwner: true);
        Assert.False(clinic.Value!.Plan.NotifyOwner);
        Assert.Equal("DHPP", clinic.Value.Plan.VaccineCode);
    }

    [Fact]
    public void Plan_Create_RejectsBlankTitle_AndBadSchedule()
    {
        Assert.Equal(ErrorCodes.ValidationFailed, TreatmentPlan.Create("t1", "p", TreatmentItemType.Medication,
            TreatmentKind.ClinicVisit, "  ", TreatmentSchedule.OnceOn(Today), Today, "u", Clock).Error);
        Assert.Equal(ErrorCodes.TreatmentScheduleInvalid, TreatmentPlan.Create("t1", "p", TreatmentItemType.Medication,
            TreatmentKind.ClinicVisit, "x", TreatmentSchedule.OnDates([]), Today, "u", Clock).Error);
    }

    [Fact]
    public void Occurrence_CanBeDoneOnce_AndCancelledPlanRefusesChanges()
    {
        var draft = TreatmentPlan.Create("t1", "p", TreatmentItemType.Vaccine, TreatmentKind.BoardingAdminister,
            "RABIES", TreatmentSchedule.OnceOn(Today), Today, "u", Clock).Value!;
        var occurrence = draft.Occurrences[0];
        Assert.True(occurrence.MarkDone(Clock.UtcNow, "u").IsSuccess);
        Assert.Equal(ErrorCodes.TreatmentOccurrenceAlreadyDone, occurrence.MarkDone(Clock.UtcNow, "u").Error);
        Assert.Equal(ErrorCodes.TreatmentOccurrenceAlreadyDone, occurrence.Skip().Error);

        Assert.True(draft.Plan.Cancel().IsSuccess);
        Assert.Equal(ErrorCodes.TreatmentPlanNotActive, draft.Plan.Cancel().Error);
        Assert.Equal(ErrorCodes.TreatmentPlanNotActive,
            draft.Plan.Update(TreatmentKind.ClinicVisit, "x", null, null, false).Error);
    }

    [Fact]
    public void CatalogItem_RejectsBuiltInCode_AndBadFigures()
    {
        var at = Clock.UtcNow;
        Assert.Equal(ErrorCodes.VaccineCatalogCodeTaken, VaccineCatalogItem.Create("t1", "dhpp", "x", at).Error);
        Assert.Equal(ErrorCodes.ValidationFailed, VaccineCatalogItem.Create("t1", "NEW", "x", at, stock: -1).Error);
        var ok = VaccineCatalogItem.Create("t1", " new1 ", " Name ", at, totalDoses: 2, boosterDays: 180, stock: 5, minStock: 2);
        Assert.True(ok.IsSuccess);
        Assert.Equal("NEW1", ok.Value!.Code);
    }
}
