using Animora.Application.Ports;
using Animora.Contracts.Vaccination;
using ClinicalDomain = Animora.Domain.Clinical;
using DomainItemType = Animora.Domain.Clinical.TreatmentItemType;
using DomainKind = Animora.Domain.Clinical.TreatmentKind;

namespace Animora.Client.Services.Vaccination;

/// <summary>Maps the treatment-plan contract types to/from the domain and Application read models.</summary>
internal static class TreatmentMapping
{
    public static DomainItemType ToDomain(this TreatmentItemType value) => value switch
    {
        TreatmentItemType.Medication => DomainItemType.Medication,
        _ => DomainItemType.Vaccine,
    };

    public static DomainKind ToDomain(this TreatmentKind value) => value switch
    {
        TreatmentKind.HomeByOwner => DomainKind.HomeByOwner,
        TreatmentKind.BoardingAdminister => DomainKind.BoardingAdminister,
        _ => DomainKind.ClinicVisit,
    };

    public static TreatmentItemType ToContract(this DomainItemType value) => value switch
    {
        DomainItemType.Medication => TreatmentItemType.Medication,
        _ => TreatmentItemType.Vaccine,
    };

    public static TreatmentKind ToContract(this DomainKind value) => value switch
    {
        DomainKind.HomeByOwner => TreatmentKind.HomeByOwner,
        DomainKind.BoardingAdminister => TreatmentKind.BoardingAdminister,
        _ => TreatmentKind.ClinicVisit,
    };

    public static ClinicalDomain.TreatmentSchedule ToDomain(this TreatmentScheduleInput input) => new(
        (ClinicalDomain.TreatmentScheduleType)(int)input.Type,
        input.Start, input.End, input.IntervalDays, input.Count, input.Days, input.WeekdayMask);

    public static CalendarOccurrence ToContract(this TreatmentCalendarRow row)
    {
        var booster = row.Source == TreatmentCalendarRowSource.VaccineBooster;
        return new(
            row.OccurrenceId, row.PlanId, row.DueDay, row.PetId, row.PetName, row.Species, row.OwnerName, row.Title,
            row.Dosage, row.ItemType.ToContract(), row.Kind.ToContract(),
            row.State == ClinicalDomain.TreatmentOccurrenceState.Done
                ? TreatmentOccurrenceState.Done
                : TreatmentOccurrenceState.Pending,
            row.NotifyOwner,
            booster ? CalendarOccurrenceSource.VaccineBooster : CalendarOccurrenceSource.PlanDay,
            booster ? row.OccurrenceId : null,
            row.VaccineName,
            row.DoseNumber);
    }

    public static TreatmentPlanSummary ToContract(this TreatmentPlanRow row) => new(
        row.PlanId, row.PetId, row.PetName, row.ItemType.ToContract(), row.Kind.ToContract(), row.Title,
        row.VaccineCode, row.Dosage, row.Notes, row.NotifyOwner,
        (TreatmentScheduleType)(int)row.ScheduleType,
        row.State switch
        {
            ClinicalDomain.TreatmentPlanState.Completed => TreatmentPlanStatus.Completed,
            ClinicalDomain.TreatmentPlanState.Cancelled => TreatmentPlanStatus.Cancelled,
            _ => TreatmentPlanStatus.Active,
        },
        row.TotalCount, row.DoneCount, row.FirstDay, row.LastDay, row.NextPendingDay);
}
