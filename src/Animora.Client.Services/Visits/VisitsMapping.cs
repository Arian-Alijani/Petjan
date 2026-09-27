using Animora.Application.UseCases.Visits.ReadModels;
using Animora.Contracts.Visits;

namespace Animora.Client.Services.Visits;

internal static class VisitsMapping
{
    public static DoctorRailInfo ToContract(this DoctorRailReadModel row) =>
        new(row.Id, row.Name, row.Specialty, Initial(row.Name), "#0d9488", row.BookedCount, 0);

    public static BookedAppointmentRef ToContract(this BookedAppointmentReadModel row) =>
        new(row.TicketId, row.AtMinutes, row.DurationMinutes, row.PetName, row.PetKind,
            Icon(row.PetKind), row.OwnerName, row.OwnerPhone, row.TypeId, row.IsUrgent,
            row.State switch
            {
                "Waiting" => BookedAppointmentState.Waiting,
                "Called" => BookedAppointmentState.Called,
                "InVisit" => BookedAppointmentState.InVisit,
                _ => BookedAppointmentState.Waiting,
            });

    public static VisitPatientRef ToContract(this TriagePatientReadModel row) =>
        new(row.PetId, row.PetName, row.PetKind, row.OwnerName, row.OwnerPhone, Icon(row.PetKind));

    public static EncounterDraft ToContract(this EncounterDraftReadModel row)
    {
        var type = VisitTypeCatalog.All.FirstOrDefault(t => t.Id == row.TypeId);
        return new EncounterDraft(row.VisitId, row.TicketId, row.DoctorId, row.DoctorName,
            row.DoctorName, row.DoctorSpecialty, Initial(row.DoctorName), "#0d9488",
            row.PetName, row.PetKind, Icon(row.PetKind), row.OwnerName, row.OwnerPhone,
            row.TypeId, type?.Label ?? row.TypeId, type?.Accent ?? "#0d9488",
            row.IsUrgent, row.IsSynthetic, row.AtMinutes, row.DurationMinutes, row.Note, row.SourceTicketId);
    }

    private static string Initial(string value) => value.Length == 0 ? string.Empty : value[..1];
    private static string Icon(string kind) => kind switch
    {
        "Dog" => "dog", "Cat" => "cat", "Bird" => "bird", "Rabbit" => "rabbit", _ => "paw",
    };
}
