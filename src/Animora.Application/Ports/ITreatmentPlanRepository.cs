using Animora.Domain.Clinical;

namespace Animora.Application.Ports;

/// <summary>Write side of treatment plans, their occurrences and the clinic's own vaccine catalog.</summary>
public interface ITreatmentPlanRepository
{
    /// <summary>The tracked plan, or <see langword="null"/>.</summary>
    Task<TreatmentPlan?> FindPlanAsync(string id, CancellationToken ct = default);

    /// <summary>The tracked occurrence, or <see langword="null"/>.</summary>
    Task<TreatmentOccurrence?> FindOccurrenceAsync(string id, CancellationToken ct = default);

    /// <summary>The plan's tracked <see cref="TreatmentOccurrenceState.Pending"/> occurrences (bounded by the schedule cap).</summary>
    Task<IReadOnlyList<TreatmentOccurrence>> ListPendingOccurrencesAsync(string planId, CancellationToken ct = default);

    /// <summary>Pending occurrences of the plan other than <paramref name="exceptOccurrenceId"/> (the one being completed).</summary>
    Task<int> CountPendingExceptAsync(string planId, string exceptOccurrenceId, CancellationToken ct = default);

    /// <summary>Queues a new plan and all its occurrences for one unit of work.</summary>
    void Add(TreatmentPlanDraft draft);

    /// <summary>Whether the clinic already has a catalog item with this (upper-case) code.</summary>
    Task<bool> CatalogCodeExistsAsync(string code, CancellationToken ct = default);

    /// <summary>Queues a new clinic catalog item.</summary>
    void AddCatalogItem(VaccineCatalogItem item);
}
