using Animora.Application.UseCases.Visits.ReadModels;
using Animora.Contracts.Visits;

namespace Animora.Client.Services.Visits;

internal static class VisitsMapping
{
    public static DoctorRailInfo ToContract(this DoctorRailReadModel row) =>
        new(row.Id, row.Name, row.Specialty, Initial(row.Name), AccentFor(row.Id), row.BookedCount, 0,
            row.HasVisitInProgress);

    // Stable per-doctor avatar accent: an id hash (not string.GetHashCode, which is randomised per process)
    // picks from a fixed teal-compatible palette so a doctor keeps one colour across runs and screens.
    private static readonly string[] AccentPalette =
        ["#0d9488", "#4f46e5", "#b45309", "#be185d", "#0369a1", "#7c3aed", "#15803d", "#c2410c"];

    internal static string AccentFor(string id)
    {
        uint hash = 2166136261;
        foreach (var ch in id)
        {
            hash = (hash ^ ch) * 16777619;
        }

        return AccentPalette[(int)(hash % (uint)AccentPalette.Length)];
    }

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
            row.DoctorName, row.DoctorSpecialty, Initial(row.DoctorName), AccentFor(row.DoctorId),
            row.PetName, row.PetKind, Icon(row.PetKind), row.OwnerName, row.OwnerPhone,
            row.TypeId, type?.Label ?? row.TypeId, type?.Accent ?? "#0d9488",
            row.IsUrgent, row.IsSynthetic, row.AtMinutes, row.DurationMinutes, row.Note, row.SourceTicketId);
    }

    // First user-perceived character (text element), so a leading surrogate pair / combining sequence is never split.
    private static string Initial(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var e = System.Globalization.StringInfo.GetTextElementEnumerator(value);
        return e.MoveNext() ? (string)e.Current : string.Empty;
    }
    private static string Icon(string kind) => kind switch
    {
        "Dog" => "dog", "Cat" => "cat", "Bird" => "bird", "Rabbit" => "rabbit", _ => "paw",
    };
}
