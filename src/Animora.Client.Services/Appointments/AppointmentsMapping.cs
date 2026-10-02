using Animora.Application.Time;
using Animora.Application.UseCases.Scheduling.ReadModels;
using Animora.Contracts.Appointments;
using Animora.Domain.Scheduling;

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
        return new AppointmentEvent(row.Id, date, minute, row.DurationMinutes, row.TypeCode,
            row.DoctorId ?? "", row.PetName, row.PetSpecies, row.OwnerName, row.OwnerPhone,
            "blue", row.PetSpecies.ToLowerInvariant(), status, row.Note ?? "", row.Id,
            0, AppointmentInvoiceState.None, AppointmentPaymentMethod.None,
            row.State == AppointmentState.NoShow ? AppointmentVisitOutcome.NoShow :
                row.State == AppointmentState.Completed ? AppointmentVisitOutcome.Complete : AppointmentVisitOutcome.None,
            lifecycle, row.VisitId,
            row.CheckedInUtcTicks is { } ticks ? new DateTimeOffset(ticks, TimeSpan.Zero) : null,
            row.CancelReason, row.CreatedByUserId, row.RescheduleCount);
    }

    /// <summary>
    /// Maps a roster row. <paramref name="index"/> is the doctor's position in the roster so each
    /// doctor gets a distinct, stable accent (roster order is deterministic).
    /// </summary>
    internal static AppointmentDoctor ToContract(this DoctorRosterReadModel row, int index) =>
        new(row.Id, row.Name, row.Name, row.Specialty,
            row.Name.Length > 0 ? row.Name[..1] : "?", AppointmentColorPalette.ByIndex(index));

    internal static BookingPatient ToContract(this BookingPatientReadModel row) =>
        new(row.Id, row.Name, row.Species, row.OwnerName, row.Phone,
            "blue", row.Species.ToLowerInvariant(), row.Species.ToLowerInvariant());

    private static string TypeLabel(string code) => code switch
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
