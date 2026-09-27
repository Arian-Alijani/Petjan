using Animora.Application.UseCases.Reception.ReadModels;
using Animora.Contracts.Reception;

namespace Animora.Client.Services.Reception;

internal static class ReceptionMapping
{
    public static QueueTicket ToContract(this QueueTicketReadModel row) => new(
        row.TicketId, row.PetName, row.PetKind, row.OwnerName, row.OwnerPhone,
        row.DoctorId ?? string.Empty, row.DoctorName ?? string.Empty, row.DoctorSpecialty ?? string.Empty,
        string.IsNullOrEmpty(row.DoctorName) ? string.Empty : row.DoctorName[..1], "#0d9488",
        row.Source switch
        {
            "Appointment" => QueueTicketSource.Appointment,
            "WalkIn" when row.IsUrgent => QueueTicketSource.Urgent,
            "WalkIn" => QueueTicketSource.WalkIn,
            _ => throw new ArgumentOutOfRangeException(nameof(row), row.Source, "Unknown ticket source"),
        },
        row.State switch
        {
            "Waiting" => QueueTicketState.Waiting,
            "Called" => QueueTicketState.Called,
            "InVisit" => QueueTicketState.InVisit,
            "Done" => QueueTicketState.Done,
            "Left" => QueueTicketState.Left,
            "NoShow" => QueueTicketState.NoShow,
            "Cancelled" => QueueTicketState.Cancelled,
            _ => throw new ArgumentOutOfRangeException(nameof(row), row.State, "Unknown ticket state"),
        },
        row.IsUrgent, row.ArrivalUtc, row.DurationMinutes, row.Sequence);
}
