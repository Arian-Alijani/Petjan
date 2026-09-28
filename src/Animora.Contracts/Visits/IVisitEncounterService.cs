namespace Animora.Contracts.Visits;

/// <summary>
/// Data seam for the clinical-encounter wizard (Phase 11): vitals → history →
/// plan → prescriptions/services (rx) → review → done. The desktop ViewModel
/// depends only on this interface; the in-memory
/// <c>MockVisitEncounterService</c> is the single swappable implementation a
/// future backend replaces (R-18). Mirrors the reference's encounter state
/// (<c>S.enc</c>) mutated by <c>renderVitals</c>/<c>renderHistory</c>/
/// <c>renderPlan</c>/<c>renderRx</c> plus <c>computeSubtotal</c>.
/// </summary>
public interface IVisitEncounterService
{
    /// <summary>Loads the current encounter draft by id (reference <c>S.enc</c>).</summary>
    Task<EncounterDraft> GetAsync(string encounterId);

    /// <summary>Persists the four vital readings (reference <c>S.enc.vitals</c>).</summary>
    Task SaveVitalsAsync(string encounterId, Vitals vitals);

    /// <summary>Persists the «شرح حال بیمار» free-text (reference <c>S.enc.history</c>).</summary>
    Task SaveHistoryNoteAsync(string encounterId, string note);

    /// <summary>Persists the «معاینهٔ فیزیکی» free-text (reference <c>S.enc.exam</c>).</summary>
    Task SaveExamNoteAsync(string encounterId, string note);

    /// <summary>Persists the «تشخیص» field (reference <c>S.enc.diagnosis</c>).</summary>
    Task SaveDiagnosisAsync(string encounterId, string diagnosis);

    /// <summary>Persists the «طرح درمان» free-text (reference <c>S.enc.plan</c>).</summary>
    Task SavePlanAsync(string encounterId, string plan);

    /// <summary>Appends a billable line to the encounter (reference <c>S.enc.services/items</c>).</summary>
    Task AddRxLineAsync(string encounterId, RxLine line);

    /// <summary>Removes a billable line by id (reference's <c>.rm</c> splice).</summary>
    Task RemoveRxLineAsync(string encounterId, string rxLineId);

    /// <summary>
    /// The selectable billable catalog behind the rx step's «افزودن خدمت…» /
    /// «افزودن کالا…» pickers (reference <c>C().SERVICES</c> + <c>C().ITEMS</c>).
    /// </summary>
    Task<IReadOnlyList<RxCatalogItem>> GetCatalogAsync();

    /// <summary>
    /// Completes the encounter and returns the success summary the done step
    /// renders (reference <c>renderDone</c> + <c>computeSubtotal</c>+9% tax).
    /// </summary>
    Task<EncounterSummary> CompleteAsync(string encounterId);

    /// <summary>Flushes pending debounced note edits before navigating away or completing a visit.</summary>
    Task FlushAsync(CancellationToken cancellationToken = default);
}
