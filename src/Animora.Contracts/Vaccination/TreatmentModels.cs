namespace Animora.Contracts.Vaccination;

/// <summary>What a treatment plan administers.</summary>
public enum TreatmentItemType
{
    Vaccine,
    Medication,
}

/// <summary>Where / by whom each day of a plan is carried out. Presentation maps each kind to its own colour.</summary>
public enum TreatmentKind
{
    /// <summary>The patient comes to the clinic.</summary>
    ClinicVisit,

    /// <summary>The owner gives it at home (an owner reminder applies).</summary>
    HomeByOwner,

    /// <summary>The patient is boarded and the clinic gives it there.</summary>
    BoardingAdminister,
}

/// <summary>How a plan's dates are produced.</summary>
public enum TreatmentScheduleType
{
    Once,
    EveryNDays,
    SpecificDates,
    WeeklyOnDays,
}

/// <summary>State of one scheduled day.</summary>
public enum TreatmentOccurrenceState
{
    Pending,
    Done,
}

/// <summary>Lifecycle of a plan.</summary>
public enum TreatmentPlanStatus
{
    Active,
    Completed,
    Cancelled,
}

/// <summary>
/// The schedule the admin picked in the registration modal. Days are plain calendar <see cref="DateOnly"/>
/// values (clinic-local); rendering them as Jalali is the View's job.
/// </summary>
/// <param name="Type">Which rule produces the dates.</param>
/// <param name="Start">First day (Once / EveryNDays / WeeklyOnDays).</param>
/// <param name="End">Inclusive last day (EveryNDays / WeeklyOnDays).</param>
/// <param name="IntervalDays">Gap in days, EveryNDays only.</param>
/// <param name="Count">Number of doses, EveryNDays only (alternative to <paramref name="End"/>).</param>
/// <param name="Days">The picked days, SpecificDates only.</param>
/// <param name="WeekdayMask">Bit <c>(int)DayOfWeek</c> per chosen weekday, WeeklyOnDays only.</param>
public sealed record TreatmentScheduleInput(
    TreatmentScheduleType Type,
    DateOnly? Start = null,
    DateOnly? End = null,
    int? IntervalDays = null,
    int? Count = null,
    IReadOnlyList<DateOnly>? Days = null,
    int WeekdayMask = 0);

/// <summary>
/// Everything the registration modal submits to create a plan. <paramref name="RescheduledDayCount"/> is how many days the
/// admin moved off the clinic weekend before submitting — recorded in the history, never used to change the dates.
/// </summary>
public sealed record TreatmentPlanInput(
    string PetId,
    TreatmentItemType ItemType,
    TreatmentKind Kind,
    string Title,
    TreatmentScheduleInput Schedule,
    string? VaccineCode = null,
    string? Dosage = null,
    string? Notes = null,
    bool NotifyOwner = false,
    int RescheduledDayCount = 0);

/// <summary>Editable fields of an active plan (the dates are fixed — cancel and recreate to change them).</summary>
public sealed record TreatmentPlanUpdate(
    string PlanId,
    TreatmentKind Kind,
    string Title,
    string? Dosage,
    string? Notes,
    bool NotifyOwner);

/// <summary>A plan of one patient with its progress counters.</summary>
public sealed record TreatmentPlanSummary(
    string PlanId,
    string PetId,
    string PetName,
    TreatmentItemType ItemType,
    TreatmentKind Kind,
    string Title,
    string? VaccineCode,
    string? Dosage,
    string? Notes,
    bool NotifyOwner,
    TreatmentScheduleType ScheduleType,
    TreatmentPlanStatus Status,
    int TotalCount,
    int DoneCount,
    DateOnly FirstDay,
    DateOnly LastDay,
    DateOnly? NextPendingDay);

/// <summary>
/// Where a calendar entry comes from. Both sources are shown by the same calendar and the same
/// appointment projection, so the clinic never sees a due dose in one screen and not in the other.
/// </summary>
public enum CalendarOccurrenceSource
{
    /// <summary>A scheduled day of a «دارو و واکسن» treatment plan (has a plan and an occurrence row).</summary>
    PlanDay,

    /// <summary>
    /// The next-dose reminder of an administered vaccination record. Read-only: it has no plan and no
    /// occurrence, so it can never be marked done or rescheduled from the calendar.
    /// </summary>
    VaccineBooster,
}

/// <summary>One scheduled day on the calendar. Carries the <see cref="Kind"/> only — colour is a presentation concern.</summary>
public sealed record CalendarOccurrence(
    string OccurrenceId,
    string PlanId,
    DateOnly Day,
    string PetId,
    string PetName,
    string Species,
    string OwnerName,
    string Title,
    string? Dosage,
    TreatmentItemType ItemType,
    TreatmentKind Kind,
    TreatmentOccurrenceState State,
    bool NotifyOwner,
    CalendarOccurrenceSource Source = CalendarOccurrenceSource.PlanDay,
    string? VaccinationRecordId = null,
    string? VaccineName = null,
    int? DoseNumber = null)
{
    /// <summary>
    /// True for a next-dose reminder projected from a vaccination record. Such an entry is derived, never
    /// stored, and its «جزئیات» is the vaccination record itself.
    /// </summary>
    public bool IsVaccineBooster => Source == CalendarOccurrenceSource.VaccineBooster;
}

/// <summary>Optional narrowing of the calendar window. Empty/null members mean "no restriction".</summary>
public sealed record CalendarFilter(
    IReadOnlyCollection<TreatmentKind>? Kinds = null,
    TreatmentItemType? ItemType = null,
    string? PetId = null);

/// <summary>A vaccine the clinic adds to its own catalog («انبار واکسن»).</summary>
public sealed record VaccineCatalogInput(
    string Code,
    string Name,
    string? Protects = null,
    int TotalDoses = 1,
    int BoosterDays = 365,
    int Stock = 0,
    int MinStock = 0);
