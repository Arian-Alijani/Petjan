namespace Animora.Domain.Events;

/// <summary>
/// A patient was registered. A10's consumer opens the patient's medical history with it.
/// </summary>
/// <param name="EventId">ULID of the event.</param>
/// <param name="TenantId">Owning tenant.</param>
/// <param name="OccurredAtUtc">When it was raised (informational — A-RULE 011).</param>
/// <param name="PetId">The new patient.</param>
/// <param name="OwnerId">The patient's owner.</param>
public sealed record PetRegisteredEvent(
    string EventId,
    string TenantId,
    DateTimeOffset OccurredAtUtc,
    string PetId,
    string OwnerId)
    : DomainEvent(EventId, TenantId, OccurredAtUtc);

/// <summary>
/// An encounter was clinically completed. A10's consumer appends the
/// <c>MedicalHistoryKind.Visit</c> history entry.
/// </summary>
/// <param name="EventId">ULID of the event.</param>
/// <param name="TenantId">Owning tenant.</param>
/// <param name="OccurredAtUtc">When it was raised (informational — A-RULE 011).</param>
/// <param name="VisitId">The completed encounter.</param>
/// <param name="PetId">The patient seen.</param>
/// <param name="OwnerId">The patient's owner.</param>
/// <param name="DoctorUserId">The attending doctor.</param>
public sealed record VisitCompletedEvent(
    string EventId,
    string TenantId,
    DateTimeOffset OccurredAtUtc,
    string VisitId,
    string PetId,
    string OwnerId,
    string DoctorUserId)
    : DomainEvent(EventId, TenantId, OccurredAtUtc);

/// <summary>
/// An encounter was handed off to Sales. Its <c>ServiceLines</c> are the invoice payload (A07/C07);
/// the visit never computes totals itself.
/// </summary>
/// <param name="EventId">ULID of the event.</param>
/// <param name="TenantId">Owning tenant.</param>
/// <param name="OccurredAtUtc">When it was raised (informational — A-RULE 011).</param>
/// <param name="VisitId">The encounter to bill.</param>
/// <param name="PetId">The patient seen.</param>
/// <param name="OwnerId">The party to invoice.</param>
public sealed record VisitReadyForInvoiceEvent(
    string EventId,
    string TenantId,
    DateTimeOffset OccurredAtUtc,
    string VisitId,
    string PetId,
    string OwnerId)
    : DomainEvent(EventId, TenantId, OccurredAtUtc);

/// <summary>
/// A vaccine dose was administered. Carries the derived due date so reminder projections need no
/// second read.
/// </summary>
/// <param name="EventId">ULID of the event.</param>
/// <param name="TenantId">Owning tenant.</param>
/// <param name="OccurredAtUtc">When it was raised (informational — A-RULE 011).</param>
/// <param name="RecordId">The vaccination record.</param>
/// <param name="PetId">The vaccinated patient.</param>
/// <param name="VaccineCode">Protocol code, uppercase.</param>
/// <param name="NextDueUtc">When the next dose is due, UTC.</param>
public sealed record VaccinationAdministeredEvent(
    string EventId,
    string TenantId,
    DateTimeOffset OccurredAtUtc,
    string RecordId,
    string PetId,
    string VaccineCode,
    DateTimeOffset? NextDueUtc)
    : DomainEvent(EventId, TenantId, OccurredAtUtc);
