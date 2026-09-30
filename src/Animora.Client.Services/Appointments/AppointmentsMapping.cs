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
        return new AppointmentEvent(row.Id, date, minute, row.DurationMinutes, row.TypeCode,
            row.DoctorId ?? "", row.PetName, row.PetSpecies, row.OwnerName, row.OwnerPhone,
            "blue", row.PetSpecies.ToLowerInvariant(), status, row.Note ?? "", row.Id,
            0, AppointmentInvoiceState.None, AppointmentPaymentMethod.None,
            row.State == AppointmentState.NoShow ? AppointmentVisitOutcome.NoShow :
                row.State == AppointmentState.Completed ? AppointmentVisitOutcome.Complete : AppointmentVisitOutcome.None);
    }

    internal static AppointmentDoctor ToContract(this DoctorRosterReadModel row) =>
        new(row.Id, row.Name, row.Name, row.Specialty,
            row.Name.Length > 0 ? row.Name[..1] : "?", "#5A67D8");

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

    internal static IReadOnlyList<AppointmentType> Types { get; } = AppointmentTypeCatalog.All
        .Select(t => new AppointmentType(t.Code, TypeLabel(t.Code), t.Code.ToLowerInvariant(),
            "#5A67D8", "#EEF2FF", "#3730A3", "calendar", t.DefaultDurationMinutes)).ToArray();
}
