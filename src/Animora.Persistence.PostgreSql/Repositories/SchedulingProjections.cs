using Animora.Domain.Identity;
using Animora.Domain.Patients;
using Animora.Domain.Sales;
using Animora.Domain.Scheduling;
using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Repositories;

internal static class SchedulingProjections
{
    // Common scalar SQL projection; no client-only method before UNION/Concat.
    internal static IQueryable<AppointmentEventProjection> AppointmentEvent(
        IQueryable<Appointment> source, AnimoraServerDbContext db) =>
        from appointment in source
        join pet in db.Set<Pet>().AsNoTracking() on appointment.PetId equals pet.Id
        join owner in db.Set<Owner>().AsNoTracking() on appointment.OwnerId equals owner.Id
        select new AppointmentEventProjection
        {
            Id = appointment.Id, StartUtc = appointment.StartUtc,
            DurationMinutes = appointment.DurationMinutes, State = appointment.State,
            TypeCode = appointment.AppointmentTypeCode, DoctorId = appointment.DoctorUserId,
            DoctorName = appointment.DoctorUserId == null ? null : db.Set<User>()
                .Where(u => u.Id == appointment.DoctorUserId).Select(u => u.DisplayName).FirstOrDefault(),
            PetId = pet.Id, PetName = pet.Name, PetSpecies = pet.Species,
            OwnerId = owner.Id, OwnerName = owner.FullName, OwnerPhone = owner.PhonePrimary,
            Note = appointment.Note,
            VisitId = appointment.VisitId, CheckedInUtc = appointment.CheckedInUtc,
            CancelReason = appointment.CancelReason, CreatedByUserId = appointment.CreatedByUserId,
            RescheduleCount = appointment.RescheduleCount, Source = appointment.Source,
            // S09 owns invoices; no financial table is implicitly introduced by scheduling.
            InvoiceTotalMinor = null, InvoicePaidMinor = null, Currency = null, InvoiceState = null,
            AggregateVersion = appointment.Version, UpdatedAt = appointment.UpdatedAt,
        };
}

internal sealed class AppointmentEventProjection
{
    public string Id { get; init; } = "";
    public DateTimeOffset StartUtc { get; init; }
    public int DurationMinutes { get; init; }
    public AppointmentState State { get; init; }
    public string TypeCode { get; init; } = "";
    public string? DoctorId { get; init; }
    public string? DoctorName { get; init; }
    public string PetId { get; init; } = "";
    public string PetName { get; init; } = "";
    public PetSpecies PetSpecies { get; init; }
    public string OwnerId { get; init; } = "";
    public string OwnerName { get; init; } = "";
    public string OwnerPhone { get; init; } = "";
    public string? Note { get; init; }
    public string? VisitId { get; init; }
    public DateTimeOffset? CheckedInUtc { get; init; }
    public string? CancelReason { get; init; }
    public string CreatedByUserId { get; init; } = "";
    public int RescheduleCount { get; init; }
    public AppointmentSource Source { get; init; }
    public long? InvoiceTotalMinor { get; init; }
    public long? InvoicePaidMinor { get; init; }
    public string? Currency { get; init; }
    public InvoiceState? InvoiceState { get; init; }
    public string? AggregateVersion { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
