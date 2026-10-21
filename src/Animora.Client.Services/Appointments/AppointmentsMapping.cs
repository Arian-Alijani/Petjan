using Animora.Application.Ports;
using Animora.Application.Time;
using Animora.Application.UseCases.Scheduling.ReadModels;
using Animora.Contracts.Appointments;
using Animora.Domain.Scheduling;
using Clinical = Animora.Domain.Clinical;

namespace Animora.Client.Services.Appointments;

internal static class AppointmentsMapping
{
    internal static AppointmentEvent ToContract(this AppointmentEventReadModel row, TimeZoneInfo tz)
    {
        var date = ClinicCalendar.LocalDay(row.StartUtcTicks, tz);
        var minute = ClinicCalendar.MinutesFromDayStart(row.StartUtcTicks, date, tz);
        var status = row.State switch
        {
            AppointmentState.Scheduled => AppointmentStatus.Pending,
            AppointmentState.Cancelled => AppointmentStatus.Cancelled,
            AppointmentState.Completed or AppointmentState.NoShow => AppointmentStatus.Done,
            _ => AppointmentStatus.Confirmed
        };
        var lifecycle = row.State switch
        {
            AppointmentState.Scheduled => AppointmentLifecycle.Scheduled,
            AppointmentState.Confirmed => AppointmentLifecycle.Confirmed,
            AppointmentState.CheckedIn => AppointmentLifecycle.CheckedIn,
            AppointmentState.InVisit => AppointmentLifecycle.InVisit,
            AppointmentState.Completed => AppointmentLifecycle.Completed,
            AppointmentState.NoShow => AppointmentLifecycle.NoShow,
            _ => AppointmentLifecycle.Cancelled
        };
        // A12: money comes only from the real appointment -> visit -> invoice link; no link means "no money data",
        // never an invented zero/estimate.
        var invoice = row.InvoiceState switch
        {
            Animora.Domain.Sales.InvoiceState.Paid => AppointmentInvoiceState.Paid,
            Animora.Domain.Sales.InvoiceState.PartiallyPaid => AppointmentInvoiceState.Partial,
            Animora.Domain.Sales.InvoiceState.Posted => AppointmentInvoiceState.Unpaid,
            _ => AppointmentInvoiceState.None
        };
        var total = row.InvoiceTotalMinor is { } minor ? (int)Math.Clamp(minor, 0, int.MaxValue) : 0;
        return new AppointmentEvent(row.Id, date, minute, row.DurationMinutes, row.TypeCode,
            row.DoctorId ?? "", row.PetName, row.PetSpecies, row.OwnerName, row.OwnerPhone,
            "blue", row.PetSpecies.ToLowerInvariant(), status, row.Note ?? "", row.Id,
            total, invoice, AppointmentPaymentMethod.None,
            row.State == AppointmentState.NoShow ? AppointmentVisitOutcome.NoShow :
                row.State == AppointmentState.Completed ? AppointmentVisitOutcome.Complete : AppointmentVisitOutcome.None,
            lifecycle, row.VisitId,
            row.CheckedInUtcTicks is { } ticks ? new DateTimeOffset(ticks, TimeSpan.Zero) : null,
            row.CancelReason, row.CreatedByUserId, row.RescheduleCount, row.DoctorName,
            row.InvoiceTotalMinor, row.InvoicePaidMinor);
    }

    /// <summary>Default minute a due item is shown at when its due date carries no time of day.</summary>
    private const int BoosterDefaultStartMinute = 9 * 60;

    internal const string BoosterIdPrefix = "vb:";

    internal const string TreatmentIdPrefix = "tp:";

    /// <summary>
    /// Projects one due item of the «دارو و واکسن» calendar into a read-only calendar entry: either a scheduled
    /// day of a treatment plan or the next-dose reminder of an administered vaccination record. Both come from
    /// the same query, so the appointment calendar and the vaccination screen can never disagree about what is
    /// due. Ids are prefixed (<c>tp:</c> / <c>vb:</c>) so they never collide with a stored appointment, and
    /// nothing is persisted.
    /// </summary>
    internal static AppointmentEvent ToDueEvent(this TreatmentCalendarRow row)
    {
        var booster = row.Source == TreatmentCalendarRowSource.VaccineBooster;
        var done = row.State == Clinical.TreatmentOccurrenceState.Done;
        var note = booster
            ? BoosterNote(row.VaccineName, row.DoseNumber)
            : string.IsNullOrWhiteSpace(row.Dosage) ? row.Title : $"{row.Title} - {row.Dosage}";

        return new AppointmentEvent(
            (booster ? BoosterIdPrefix : TreatmentIdPrefix) + row.OccurrenceId,
            row.DueDay,
            BoosterDefaultStartMinute,
            30,
            booster ? AppointmentTypeIds.VaccineBooster : TreatmentTypeId(row.Kind),
            "",
            row.PetName,
            row.Species,
            row.OwnerName,
            "",
            "blue",
            row.Species.ToLowerInvariant(),
            done ? AppointmentStatus.Done : AppointmentStatus.Pending,
            note,
            row.OccurrenceId,
            0,
            AppointmentInvoiceState.None,
            AppointmentPaymentMethod.None,
            done ? AppointmentVisitOutcome.Complete : AppointmentVisitOutcome.None,
            done ? AppointmentLifecycle.Completed : AppointmentLifecycle.Scheduled,
            PetId: row.PetId,
            IsVaccineBooster: booster,
            VaccineName: booster ? row.VaccineName : row.ItemType == Clinical.TreatmentItemType.Vaccine ? row.Title : null,
            TreatmentOccurrenceId: booster ? null : row.OccurrenceId,
            TreatmentPlanId: booster ? null : row.PlanId);
    }

    private static string BoosterNote(string? vaccineName, int? doseNumber) => doseNumber is { } dose
        ? $"{vaccineName} - دوز {dose}"
        : vaccineName ?? string.Empty;

    private static string TreatmentTypeId(Clinical.TreatmentKind kind) => kind switch
    {
        Clinical.TreatmentKind.HomeByOwner => AppointmentTypeIds.TreatmentHome,
        Clinical.TreatmentKind.BoardingAdminister => AppointmentTypeIds.TreatmentBoarding,
        _ => AppointmentTypeIds.TreatmentClinic,
    };

    /// <summary>
    /// The synthetic, non-bookable types treatment-plan days render with — accents match the vaccination screen's
    /// kind colours (clinic = blue, home = green, boarding = amber).
    /// </summary>
    internal static IReadOnlyList<AppointmentType> TreatmentTypes { get; } =
    [
        new(AppointmentTypeIds.TreatmentClinic, "دارو/واکسن · کلینیک", "treatment-clinic", "#1570cd",
            AppointmentColorPalette.Tint("#1570cd"), AppointmentColorPalette.Shade("#1570cd"), "IconSyringe", 30),
        new(AppointmentTypeIds.TreatmentHome, "دارو/واکسن · منزل", "treatment-home", "#027a48",
            AppointmentColorPalette.Tint("#027a48"), AppointmentColorPalette.Shade("#027a48"), "IconSyringe", 30),
        new(AppointmentTypeIds.TreatmentBoarding, "دارو/واکسن · پانسیون", "treatment-boarding", "#b54708",
            AppointmentColorPalette.Tint("#b54708"), AppointmentColorPalette.Shade("#b54708"), "IconSyringe", 30),
    ];

    /// <summary>The synthetic, non-bookable type the booster entries render with (distinct accent + syringe).</summary>
    internal static AppointmentType BoosterType { get; } = new(
        AppointmentTypeIds.VaccineBooster, "یادآور واکسن تکرارشونده", "vaccine-booster", "#0E9F6E",
        AppointmentColorPalette.Tint("#0E9F6E"), AppointmentColorPalette.Shade("#0E9F6E"), "IconSyringe", 30);

    /// <summary>
    /// Maps a roster row. <paramref name="index"/> is the doctor's position in the roster so each
    /// doctor gets a distinct, stable accent (roster order is deterministic).
    /// </summary>
    internal static AppointmentDoctor ToContract(this DoctorRosterReadModel row, int index) =>
        new(row.Id, row.Name, row.Name, row.Specialty,
            row.Name.Length > 0 ? row.Name[..1] : "?", AppointmentColorPalette.ByIndex(index),
            row.WorkStartMinute, row.WorkEndMinute, row.WorkDaysMask);

    internal static BookingPatient ToContract(this BookingPatientReadModel row) =>
        new(row.Id, row.Name, row.Species, row.OwnerName, row.Phone,
            "blue", row.Species.ToLowerInvariant(), row.Species.ToLowerInvariant());

    /// <summary>Single source of truth for the Persian label of a built-in appointment/visit type code.</summary>
    internal static string TypeLabel(string code) => code switch
    {
        "CHECKUP" => "معاینه",
        "VACCINE" => "واکسیناسیون",
        "SURGERY" => "جراحی",
        "DENTAL" => "دندان‌پزشکی",
        "GROOMING" => "آرایش و شست‌وشو",
        "FOLLOWUP" => "پیگیری",
        "CONSULT" => "مشاوره",
        _ => "سایر"
    };

    /// <summary>Builds a type whose background/foreground are derived from its single accent color.</summary>
    internal static AppointmentType BuildType(string id, string label, string classKey, string accent, int durationMinutes) =>
        new(id, label, classKey, accent, AppointmentColorPalette.Tint(accent),
            AppointmentColorPalette.Shade(accent), "calendar", durationMinutes);

    // Each catalog type gets its own accent (by catalog order) — stable across runs.
    internal static IReadOnlyList<AppointmentType> Types { get; } = AppointmentTypeCatalog.All
        .Select((t, i) => BuildType(t.Code, TypeLabel(t.Code), t.Code.ToLowerInvariant(),
            AppointmentColorPalette.ByIndex(i), t.DefaultDurationMinutes)).ToArray();
}
