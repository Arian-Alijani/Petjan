using Animora.Application.Ports;
using Animora.Application.UseCases.Reception.ReadModels;
using Animora.Domain.Identity;
using Animora.Domain.Patients;
using Animora.Domain.Scheduling;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Queries;

/// <summary>
/// C02 reception-queue projection. The queue is a <b>derived</b> view built in a single logical
/// statement: today's appointments LEFT-JOINed to their (lazily-created) <c>ReceptionQueueEntry</c>
/// row, then <c>Concat</c> (UNION ALL) the walk-in rows (no <c>AppointmentId</c>). The day filter is
/// a UTC half-open range on an indexed timestamp — never a SQL date function, never a per-ticket
/// round trip. Tenant/soft-delete filters are applied automatically by ModelConventions.
/// </summary>
public sealed class ReceptionQueueQueries(AnimoraLocalDbContext db) : IReceptionQueueQueries
{
    public async Task<IReadOnlyList<QueueTicketReadModel>> GetQueueAsync(
        DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken ct)
    {
        // Leg 1 — appointment-sourced tickets: today's appointments LEFT JOIN their queue row.
        // State comes from the queue row when it exists, else the appointment maps to Waiting.
        var fromAppointments =
            from a in db.Set<Appointment>().AsNoTracking()
            where a.StartUtc >= startUtc && a.StartUtc < endUtc
                && a.State != AppointmentState.Cancelled
            join qe in db.Set<ReceptionQueueEntry>().AsNoTracking()
                on a.Id equals qe.AppointmentId into qej
            from q in qej.DefaultIfEmpty()
            join pet in db.Set<Pet>().AsNoTracking() on a.PetId equals pet.Id
            join owner in db.Set<Owner>().AsNoTracking() on a.OwnerId equals owner.Id
            select new QueueTicketReadModel(
                "apt:" + a.Id,
                "Appointment",
                q == null
                    ? (a.State == AppointmentState.NoShow ? "NoShow" : "Waiting")
                    : q.State.ToString(),
                q != null && q.Priority >= ReceptionQueueEntry.PriorityEmergency,
                pet.Id,
                pet.Name,
                pet.Species.ToString(),
                owner.Id,
                owner.FullName,
                owner.PhonePrimary,
                a.DoctorUserId,
                a.DoctorUserId == null
                    ? null
                    : db.Set<User>().Where(u => u.Id == a.DoctorUserId).Select(u => u.DisplayName).FirstOrDefault(),
                null, // DoctorSpecialty: no domain source in C02 — mapping/display derives it.
                q == null ? a.StartUtc : q.EnqueuedUtc,
                a.DurationMinutes,
                q == null ? 0 : q.TicketNumber);

        // Leg 2 — walk-in tickets: queue rows with no appointment, enqueued in range.
        var fromWalkIns =
            from q in db.Set<ReceptionQueueEntry>().AsNoTracking()
            where q.AppointmentId == null
                && q.EnqueuedUtc >= startUtc && q.EnqueuedUtc < endUtc
            join pet in db.Set<Pet>().AsNoTracking() on q.PetId equals pet.Id
            join owner in db.Set<Owner>().AsNoTracking() on q.OwnerId equals owner.Id
            select new QueueTicketReadModel(
                "wlk:" + q.Id,
                "WalkIn",
                q.State.ToString(),
                q.Priority >= ReceptionQueueEntry.PriorityEmergency,
                pet.Id,
                pet.Name,
                pet.Species.ToString(),
                owner.Id,
                owner.FullName,
                owner.PhonePrimary,
                q.DoctorUserId,
                q.DoctorUserId == null
                    ? null
                    : db.Set<User>().Where(u => u.Id == q.DoctorUserId).Select(u => u.DisplayName).FirstOrDefault(),
                null,
                q.EnqueuedUtc,
                0,
                q.TicketNumber);

        var rows = await fromAppointments.Concat(fromWalkIns)
            .OrderBy(t => t.ArrivalUtc).ThenBy(t => t.TicketId)
            .ToListAsync(ct);
        return rows;
    }

    public async Task<IReadOnlyList<ReceptionDoctorReadModel>> GetDoctorsAsync(CancellationToken ct)
    {
        var doctors =
            from m in db.Set<StaffMembership>().AsNoTracking()
            where m.Role == StaffRole.Doctor && m.Status == MembershipStatus.Active
            join u in db.Set<User>().AsNoTracking() on m.UserId equals u.Id
            orderby u.DisplayName, u.Id
            select new ReceptionDoctorReadModel(u.Id, u.DisplayName, string.Empty);

        return await doctors.ToListAsync(ct);
    }
}
