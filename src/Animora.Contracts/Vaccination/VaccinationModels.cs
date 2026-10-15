namespace Animora.Contracts.Vaccination;

/// <summary>
/// The urgency state of a vaccination record, mirroring the reference's
/// <c>status</c> field (Animora-UI/assets/vaccination-data.js): a record is
/// <c>done</c> once administered, otherwise derived from its due date
/// (<c>overdue</c> when past, <c>due</c> within 14 days, <c>scheduled</c>
/// further out). Drives the segmented filter and the status pill.
/// </summary>
public enum VaccinationStatus
{
    Due,
    Overdue,
    Scheduled,
    Done,
}

/// <summary>
/// The SMS-reminder state of a record, mirroring the reference's
/// <c>smsState</c> field (<c>none | sent | scheduled | failed</c>) that the
/// «پیامک» table cell renders.
/// </summary>
public enum SmsState
{
    None,
    Sent,
    Scheduled,
    Failed,
}

/// <summary>
/// One vaccination record, mirroring the fields the reference's
/// <c>renderRows</c>/<c>dueCell</c> read from <c>VAX_RECORDS</c> plus the
/// catalog reference (<see cref="VaccineId"/>) and the detail modal
/// (<c>lastDate</c>/<c>lot</c>/<c>site</c>/<c>vet</c>/<c>mobile</c>). Dates are
/// carried as real <see cref="DateOnly"/> values (the seam a future backend
/// fills); the View renders them through the Phase 04 Jalali helpers.
/// </summary>
public sealed record VaccinationRecord(
    string Id,
    string PetName,
    string Species,
    string OwnerName,
    string OwnerMobile,
    string VaccineId,
    string Dose,
    DateOnly? LastDate,
    DateOnly DueDate,
    string VetName,
    string Lot,
    string Site,
    string Tone,
    VaccinationStatus Status,
    SmsState SmsState);

/// <summary>
/// A vaccine product in the clinic's catalog, mirroring a row of the
/// reference's <c>VAX_CATALOG</c>: identity, the protected-diseases blurb, the
/// stock/minimum levels the stock bar renders and the per-species icon.
/// </summary>
public sealed record VaccineCatalogEntry(
    string Id,
    string Name,
    string Short,
    string Species,
    string Tone,
    string Protects,
    int BoosterMonths,
    int Stock,
    int MinStock)
{
    public bool IsOutOfStock => Stock <= 0;
}

/// <summary>
/// An SMS message template, mirroring a row of the reference's
/// <c>VAX_SMS_TEMPLATES</c>. <see cref="Body"/> carries the token placeholders
/// (<c>{owner}</c>/<c>{pet}</c>/<c>{vaccine}</c>/<c>{date}</c>/<c>{next}</c>)
/// that the service substitutes when building a preview.
/// </summary>
public sealed record SmsTemplate(
    string Id,
    string Name,
    string Channel,
    string Body);

/// <summary>
/// One reminder SMS recorded for a pet of an owner — exactly what is persisted
/// (when, pet, vaccine, channel and the composed text); nothing is invented.
/// </summary>
public sealed record OwnerSmsReminder(
    string Id,
    DateTime When,
    string PetId,
    string PetName,
    string VaccineName,
    string Channel,
    string? Body);

/// <summary>
/// The composed SMS preview returned by
/// <see cref="IVaccinationService.BuildSmsPreviewAsync"/>: the rendered body
/// plus its character count and derived SMS-segment count (70 chars/segment),
/// mirroring the reference's <c>openSmsCompose.paint</c>.
/// </summary>
public sealed record SmsPreview(
    string RecordId,
    string TemplateId,
    string Body,
    int CharacterCount,
    int SegmentCount);
