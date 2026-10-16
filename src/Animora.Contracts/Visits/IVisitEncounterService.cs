namespace Animora.Contracts.Visits;

/// <summary>
/// Data seam for the clinical-encounter wizard (Phase 11): vitals → history →
/// plan → prescriptions/services (rx) → review → done. The desktop ViewModel
/// depends only on this interface; a local dispatcher-backed service implements
/// the seam (R-18). Mirrors the reference's encounter state
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

    /// <summary>Lists the radiology studies recorded on the encounter, oldest first («رادیولوژی»).</summary>
    Task<IReadOnlyList<RadiologyEntry>> GetRadiologyAsync(string encounterId);

    /// <summary>
    /// Records a radiology study on the encounter and returns its id. Journaled, so the entry reaches
    /// the server and every other client on the next sync.
    /// </summary>
    Task<string> AddRadiologyAsync(string encounterId, RadiologyEntry entry);

    /// <summary>Removes a radiology study by id (journaled).</summary>
    Task RemoveRadiologyAsync(string encounterId, string radiologyId);

    /// <summary>
    /// Stores an attachment's bytes in the content-addressed local file store and returns the
    /// <see cref="RadiologyFile"/> reference (hash + name + content type) to put on a radiology entry.
    /// </summary>
    Task<RadiologyFile> StoreAttachmentAsync(Stream content, string fileName, string contentType, CancellationToken cancellationToken = default);

    /// <summary>Opens a previously stored attachment for reading; the caller disposes the stream.</summary>
    Task<Stream> OpenAttachmentAsync(string hash, CancellationToken cancellationToken = default);

    /// <summary>Lists the laboratory result sheets recorded on the encounter, oldest first («آزمایشات»).</summary>
    Task<IReadOnlyList<LabEntry>> GetLabsAsync(string encounterId);

    /// <summary>
    /// Records a laboratory result sheet on the encounter and returns its id. Journaled, so the entry reaches the
    /// server and every other client on the next sync. The selectable test types come from <see cref="ILabTestSettingsService"/>.
    /// </summary>
    Task<string> AddLabAsync(string encounterId, LabEntry entry);

    /// <summary>Removes a laboratory result sheet by id (journaled).</summary>
    Task RemoveLabAsync(string encounterId, string labId);

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
