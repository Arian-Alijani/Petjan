namespace Animora.Contracts.Vaccination;

/// <summary>
/// Data seam for the Vaccination screen (due/overdue list + vaccine catalog +
/// SMS reminders). The desktop ViewModel depends only on this interface;
/// a local dispatcher-backed service implements the seam (R-18).
/// </summary>
public interface IVaccinationService
{
    /// <summary>
    /// The clinic's vaccination records (one per pet/vaccine appointment),
    /// mirroring the reference's <c>VAX_RECORDS</c> collection that the
    /// segmented filter, records table and counts derive from.
    /// </summary>
    Task<IReadOnlyList<VaccinationRecord>> GetRecordsAsync();

    /// <summary>
    /// The vaccine product catalog shown on the «انبار واکسن» tab, mirroring
    /// the reference's <c>VAX_CATALOG</c> collection.
    /// </summary>
    Task<IReadOnlyList<VaccineCatalogEntry>> GetCatalogAsync();

    /// <summary>
    /// The SMS message templates shown on the «تنظیمات» tab and the compose
    /// drawer, mirroring the reference's <c>VAX_SMS_TEMPLATES</c>.
    /// </summary>
    Task<IReadOnlyList<SmsTemplate>> GetSmsTemplatesAsync();

    /// <summary>
    /// Records one administered vaccine dose for a patient directly (the pet file's «ثبت واکسن»). The dose is
    /// written as a <c>VaccinationRecord</c> plus its «Dose administered» ledger entry, so it appears in this
    /// module's history and due list exactly like a dose recorded here. Returns the new record id.
    /// </summary>
    Task<string> RegisterDoseAsync(VaccinationDoseInput input);

    /// <summary>
    /// Marks a record as administered (<c>status = done</c>), mirroring the
    /// reference's <c>vaxMarkDone</c>. Returns the updated record.
    /// </summary>
    Task<VaccinationRecord> MarkDoneAsync(string recordId);

    /// <summary>
    /// Builds the composed SMS body for <paramref name="recordId"/> using
    /// <paramref name="templateId"/>, mirroring the reference's
    /// <c>buildPreview</c> (token substitution) plus its character/segment
    /// count.
    /// </summary>
    Task<SmsPreview> BuildSmsPreviewAsync(string recordId, string templateId);

    /// <summary>
    /// Records that an SMS reminder was sent for <paramref name="recordId"/>
    /// (<c>smsState = sent</c>). The actual network transmission is stubbed in
    /// the mock per the phase file (no real gateway); only the persistence of
    /// the sent state is modeled here. Returns the updated record.
    /// </summary>
    Task<VaccinationRecord> SendSmsAsync(string recordId, string message);

    /// <summary>
    /// The reminder SMS messages recorded for the pets of <paramref name="ownerId"/>,
    /// newest first and bounded server-side (owner file «پیامک‌های ارسال‌شده»).
    /// </summary>
    Task<IReadOnlyList<OwnerSmsReminder>> GetSmsHistoryForOwnerAsync(string ownerId);

    /// <summary>
    /// One ordered page of the vaccination history ledger (administered /
    /// scheduled / SMS-sent / rescheduled) plus the total computed over the
    /// <b>whole</b> filtered set. The service filters/sorts/pages on the caller's
    /// behalf so no unbounded result set ever crosses the seam (PERFORMANCE.md §6).
    /// </summary>
    Task<VaccinationHistoryPage> GetHistoryAsync(VaccinationHistoryFilter filter);

    /// <summary>
    /// Schedules a medication / vaccine for a patient and expands its schedule into calendar days.
    /// Returns the new plan id.
    /// </summary>
    Task<string> CreateTreatmentPlanAsync(TreatmentPlanInput input);

    /// <summary>Edits the descriptive fields of an active plan.</summary>
    Task UpdateTreatmentPlanAsync(TreatmentPlanUpdate update);

    /// <summary>Cancels a plan; its still-pending days are withdrawn, given ones are kept.</summary>
    Task CancelTreatmentPlanAsync(string planId);

    /// <summary>
    /// The scheduled days in <paramref name="from"/>..<paramref name="to"/> (inclusive, at most 62 days),
    /// narrowed by <paramref name="filter"/>. Always bounded server-side.
    /// </summary>
    Task<IReadOnlyList<CalendarOccurrence>> GetCalendarAsync(DateOnly from, DateOnly to, CalendarFilter? filter = null);

    /// <summary>Records that one scheduled day was carried out.</summary>
    Task MarkOccurrenceDoneAsync(string occurrenceId);

    /// <summary>The patient's plans, newest first (bounded).</summary>
    Task<IReadOnlyList<TreatmentPlanSummary>> GetPlansForPetAsync(string petId);

    /// <summary>Adds a vaccine to the clinic's own catalog; returns the new item id.</summary>
    Task<string> AddCatalogVaccineAsync(VaccineCatalogInput input);
}
