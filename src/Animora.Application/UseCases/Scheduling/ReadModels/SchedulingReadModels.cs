using Animora.Domain.Scheduling;

namespace Animora.Application.UseCases.Scheduling.ReadModels;

public sealed record AppointmentEventReadModel(
    string Id, long StartUtcTicks, int DurationMinutes, AppointmentState State,
    string TypeCode, string? DoctorId, string? DoctorName,
    string PetId, string PetName, string PetSpecies, string OwnerId,
    string OwnerName, string OwnerPhone, string? Note);

public sealed record AppointmentDetailReadModel(AppointmentEventReadModel Event, string CreatedText);
public sealed record TimeSlotReadModel(int Minutes, bool IsFree);
public sealed record DoctorRosterReadModel(string Id, string Name, string Specialty);

public sealed record BookingPatientReadModel(string Id, string OwnerId, string Name, string Species,
    string OwnerName, string Phone);
