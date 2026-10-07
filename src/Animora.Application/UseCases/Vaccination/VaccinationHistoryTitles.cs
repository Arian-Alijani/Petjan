using Animora.Contracts.Vaccination;

namespace Animora.Application.UseCases.Vaccination;

/// <summary>
/// The stable <c>MedicalHistoryEntry.Title</c> keys of the vaccination ledger. Commands write them, the ledger query filters
/// by them and the client maps them back to <see cref="VaccinationHistoryKind"/> — one definition so the three never drift.
/// </summary>
public static class VaccinationHistoryTitles
{
    /// <summary>A vaccine dose was given (a legacy record or a plan day).</summary>
    public const string DoseAdministered = "Dose administered";

    /// <summary>A medication day of a plan was given.</summary>
    public const string MedicationGiven = "Medication given";

    /// <summary>A reminder message was produced for the owner.</summary>
    public const string ReminderProduced = "Reminder produced";

    /// <summary>A treatment plan was registered (its days are on the calendar).</summary>
    public const string DoseScheduled = "Dose scheduled";

    /// <summary>Registered days that fell on the clinic weekend were moved to working days.</summary>
    public const string DoseRescheduled = "Dose rescheduled";

    /// <summary>The descriptive fields of a plan were edited.</summary>
    public const string TreatmentUpdated = "Treatment updated";

    /// <summary>A plan was cancelled; its pending days were removed.</summary>
    public const string TreatmentCancelled = "Treatment cancelled";

    /// <summary>The titles that make up one ledger kind.</summary>
    public static IReadOnlyList<string> For(VaccinationHistoryKind kind) => kind switch
    {
        VaccinationHistoryKind.Administered => [DoseAdministered, MedicationGiven],
        VaccinationHistoryKind.SmsSent => [ReminderProduced],
        VaccinationHistoryKind.Scheduled => [DoseScheduled],
        VaccinationHistoryKind.Rescheduled => [DoseRescheduled],
        VaccinationHistoryKind.Updated => [TreatmentUpdated],
        VaccinationHistoryKind.Cancelled => [TreatmentCancelled],
        _ => [],
    };

    /// <summary>The ledger kind of a stored title (unknown titles read as «Scheduled», the neutral lifecycle event).</summary>
    public static VaccinationHistoryKind KindOf(string title) => title switch
    {
        DoseAdministered or MedicationGiven => VaccinationHistoryKind.Administered,
        ReminderProduced => VaccinationHistoryKind.SmsSent,
        DoseRescheduled => VaccinationHistoryKind.Rescheduled,
        TreatmentUpdated => VaccinationHistoryKind.Updated,
        TreatmentCancelled => VaccinationHistoryKind.Cancelled,
        _ => VaccinationHistoryKind.Scheduled,
    };
}
