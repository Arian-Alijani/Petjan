using Animora.Domain.Sales;
using Animora.Domain.Scheduling;

namespace Animora.Application.UseCases.Scheduling.ReadModels;

public sealed record AppointmentEventReadModel(
    string Id, long StartUtcTicks, int DurationMinutes, AppointmentState State,
    string TypeCode, string? DoctorId, string? DoctorName,
    string PetId, string PetName, string PetSpecies, string OwnerId,
    string OwnerName, string OwnerPhone, string? Note,
    string? VisitId = null, long? CheckedInUtcTicks = null, string? CancelReason = null,
    string CreatedByUserId = "", int RescheduleCount = 0,
    AppointmentSource Source = AppointmentSource.Phone,
    long? InvoiceTotalMinor = null, long? InvoicePaidMinor = null, string? Currency = null,
    InvoiceState? InvoiceState = null);

/// <summary>
/// Appointment detail. <paramref name="AggregateVersion"/> is the opaque concurrency token observed at read
/// time (A29); <paramref name="CreatedByName"/> is the resolved creator display name, or null when unknown.
/// </summary>
public sealed record AppointmentDetailReadModel(AppointmentEventReadModel Event, string CreatedText,
    string? AggregateVersion = null, string? CreatedByName = null);
public sealed record TimeSlotReadModel(int Minutes, bool IsFree);
public sealed record DoctorRosterReadModel(string Id, string Name, string Specialty);

public sealed record BookingPatientReadModel(string Id, string OwnerId, string Name, string Species,
    string OwnerName, string Phone);
