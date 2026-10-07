using Animora.Domain.Clinical;

namespace Animora.Application.Ports;

/// <summary>One scheduled day on the «دارو و واکسن» calendar, joined with its patient.</summary>
public sealed record TreatmentCalendarRow(string OccurrenceId, string PlanId, DateOnly DueDay, string PetId,
    string PetName, string Species, string OwnerName, string Title, string? Dosage, TreatmentItemType ItemType,
    TreatmentKind Kind, TreatmentOccurrenceState State, DateTimeOffset? DoneUtc, bool NotifyOwner);

/// <summary>A plan with its progress counters (for the patient's plan list).</summary>
public sealed record TreatmentPlanRow(string PlanId, string PetId, string PetName, TreatmentItemType ItemType,
    TreatmentKind Kind, string Title, string? VaccineCode, string? Dosage, string? Notes, bool NotifyOwner,
    TreatmentScheduleType ScheduleType, TreatmentPlanState State, int TotalCount, int DoneCount,
    DateOnly FirstDay, DateOnly LastDay, DateOnly? NextPendingDay, DateTimeOffset CreatedUtc);

/// <summary>A clinic-added catalog vaccine with its stock levels.</summary>
public sealed record CustomVaccineRow(string Code, string Name, string? Protects, int TotalDoses, int BoosterDays,
    int Stock, int MinStock);

/// <summary>Read side of treatment plans. Every method is tenant-filtered and bounded.</summary>
public interface ITreatmentPlanQueries
{
    /// <summary>
    /// Occurrences with <c>from &lt;= DueDay &lt;= to</c> (ordered by day, then plan), optionally narrowed by kind,
    /// item type and patient. Cancelled/skipped occurrences are excluded. At most <paramref name="take"/> rows.
    /// </summary>
    Task<IReadOnlyList<TreatmentCalendarRow>> GetCalendarAsync(DateOnly from, DateOnly to,
        IReadOnlyCollection<TreatmentKind>? kinds, TreatmentItemType? itemType, string? petId, int take, CancellationToken ct);

    /// <summary>The patient's plans, newest first, at most <paramref name="take"/>.</summary>
    Task<IReadOnlyList<TreatmentPlanRow>> GetPlansForPetAsync(string petId, int take, CancellationToken ct);

    /// <summary>The clinic's own catalog additions (active ones), ordered by name.</summary>
    Task<IReadOnlyList<CustomVaccineRow>> GetCustomCatalogAsync(CancellationToken ct);
}
