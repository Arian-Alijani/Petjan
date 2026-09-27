using Animora.Application.Ports;
using Animora.Application.UseCases.Reception.ReadModels;
using Animora.Domain.Identity;
using Animora.Domain.Patients;
using Animora.Domain.Scheduling;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Queries;

/// <summary>One derived queue statement: appointment LEFT JOIN queue UNION ALL walk-ins.</summary>
public sealed class ReceptionQueueQueries(AnimoraLocalDbContext db) : IReceptionQueueQueries
{
    public async Task<IReadOnlyList<QueueTicketReadModel>> GetQueueAsync(
        DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken ct)
    {
        // Keep both legs as SQL-only scalar projections until after Concat: enum.ToString() and
        // record constructors in a leg force client evaluation before UNION ALL in EF Core.
        var appointments =
            from appointment in db.Set<Appointment>().AsNoTracking()
            where appointment.StartUtc >= startUtc && appointment.StartUtc < endUtc
                && appointment.State != AppointmentState.Cancelled
            join entry in db.Set<ReceptionQueueEntry>().AsNoTracking()
                on appointment.Id equals entry.AppointmentId into entries
            from row in entries.DefaultIfEmpty()
            join pet in db.Set<Pet>().AsNoTracking() on appointment.PetId equals pet.Id
            join owner in db.Set<Owner>().AsNoTracking() on appointment.OwnerId equals owner.Id
            select new
            {
                TicketId = "apt:" + appointment.Id, Source = 0,
                State = row == null ? appointment.State == AppointmentState.NoShow ? 5 : 0 : (int)row.State,
                Urgent = row != null && row.Priority > 0,
                PetId = pet.Id, PetName = pet.Name, PetKind = pet.Species,
                OwnerId = owner.Id, OwnerName = owner.FullName, OwnerPhone = owner.PhonePrimary,
                DoctorId = appointment.DoctorUserId,
                DoctorName = appointment.DoctorUserId == null ? null : db.Set<User>()
                    .Where(u => u.Id == appointment.DoctorUserId).Select(u => u.DisplayName).FirstOrDefault(),
                Arrival = row == null ? appointment.StartUtc : row.EnqueuedUtc,
                Duration = appointment.DurationMinutes, Sequence = row == null ? 0 : row.TicketNumber,
            };
        var walkIns =
            from row in db.Set<ReceptionQueueEntry>().AsNoTracking()
            where row.AppointmentId == null && row.EnqueuedUtc >= startUtc && row.EnqueuedUtc < endUtc
            join pet in db.Set<Pet>().AsNoTracking() on row.PetId equals pet.Id
            join owner in db.Set<Owner>().AsNoTracking() on row.OwnerId equals owner.Id
            select new
            {
                TicketId = "wlk:" + row.Id, Source = 1, State = (int)row.State,
                Urgent = row.Priority > 0, PetId = pet.Id, PetName = pet.Name, PetKind = pet.Species,
                OwnerId = owner.Id, OwnerName = owner.FullName, OwnerPhone = owner.PhonePrimary,
                DoctorId = row.DoctorUserId,
                DoctorName = row.DoctorUserId == null ? null : db.Set<User>()
                    .Where(u => u.Id == row.DoctorUserId).Select(u => u.DisplayName).FirstOrDefault(),
                Arrival = row.EnqueuedUtc, Duration = 0, Sequence = row.TicketNumber,
            };
        var rows = await appointments.Concat(walkIns).ToListAsync(ct);
        return rows.OrderBy(r => r.Arrival).ThenBy(r => r.TicketId).Select(r =>
            new QueueTicketReadModel(r.TicketId, r.Source == 0 ? "Appointment" : "WalkIn",
                r.Source == 0 && r.State == 5 ? "NoShow" : ((QueueState)r.State).ToString(),
                r.Urgent, r.PetId, r.PetName, r.PetKind.ToString(), r.OwnerId, r.OwnerName,
                r.OwnerPhone, r.DoctorId, r.DoctorName, null, r.Arrival, r.Duration, r.Sequence))
            .ToArray();
    }

    public async Task<IReadOnlyList<ReceptionDoctorReadModel>> GetDoctorsAsync(CancellationToken ct)
    {
        var doctors = from membership in db.Set<StaffMembership>().AsNoTracking()
                      where membership.Role == StaffRole.Doctor && membership.Status == MembershipStatus.Active
                      join user in db.Set<User>().AsNoTracking() on membership.UserId equals user.Id
                      orderby user.DisplayName, user.Id
                      select new ReceptionDoctorReadModel(user.Id, user.DisplayName, string.Empty);
        return await doctors.ToListAsync(ct);
    }
}
