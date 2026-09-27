namespace Animora.Application.UseCases.Reception.ReadModels;

/// <summary>
/// One reception-queue ticket, projected in a single statement from today's appointments
/// LEFT-JOINed to their (lazily-created) <c>ReceptionQueueEntry</c> rows, UNION-ALL the walk-in
/// rows (C02). Carries every field the Contracts <c>QueueTicket</c> needs except the presentation
/// bits (doctor initials/accent) which mapping derives. <see cref="TicketId"/> is already the
/// composite (<c>"apt:"</c>/<c>"wlk:"</c>) built by the projection via <c>TicketRef</c>.
/// </summary>
public sealed record QueueTicketReadModel(
    string TicketId,
    string Source,
    string State,
    bool IsUrgent,
    string PetId,
    string PetName,
    string PetKind,
    string OwnerId,
    string OwnerName,
    string OwnerPhone,
    string? DoctorId,
    string? DoctorName,
    string? DoctorSpecialty,
    DateTimeOffset ArrivalUtc,
    int DurationMinutes,
    int Sequence);

/// <summary>
/// One doctor available for assignment on the reception walk-in modal — projected from
/// <c>StaffMembership</c> rows with <c>StaffRole.Doctor</c> and <c>MembershipStatus.Active</c> (C02).
/// </summary>
public sealed record ReceptionDoctorReadModel(
    string Id,
    string Name,
    string Specialty);
