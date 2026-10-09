using Animora.Application.UseCases.Scheduling;
using Animora.Application.UseCases.Boarding;
using Animora.Domain.Boarding;
using Animora.Domain.Identity;
using Animora.Domain.Patients;
using Animora.Domain.Results;
using Animora.Domain.Scheduling;
using Animora.Persistence.PostgreSql.Context;
using Animora.Persistence.PostgreSql.Concurrency;
using Microsoft.EntityFrameworkCore;
namespace Animora.Server.Commands;

/// <summary>
/// Optimistic uuid fences on doctor membership / rooms. A same-value UPDATE obtains the write fence;
/// its version predicate detects a contender and retries the WHOLE intake transaction before re-reading
/// occupancy/slots. No raw SQL, advisory locks, or SELECT FOR UPDATE. Stable resource order avoids deadlocks.
/// </summary>
internal sealed class ServerSchedulingConcurrencyGuard(AnimoraServerDbContext db) : IServerCommandConcurrencyGuard
{
    public async Task<Result> PrepareAsync(object command, CancellationToken ct = default)
    {
        string? doctor = null; var roomIds = new HashSet<string>(StringComparer.Ordinal);
        switch (command)
        {
            case BookAppointmentCommand book:
                var pet = await db.Set<Pet>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == book.PetId, ct).ConfigureAwait(false);
                if (pet is null) return Result.Fail(ErrorCodes.PetNotFound);
                if (pet.OwnerId != book.OwnerId) return Result.Fail(ErrorCodes.ValidationFailed, "patient owner mismatch");
                doctor = book.DoctorId;
                break;
            case RescheduleAppointmentCommand move:
                doctor = await db.Set<Appointment>().AsNoTracking().Where(a => a.Id == move.AppointmentId)
                    .Select(a => a.DoctorUserId).FirstOrDefaultAsync(ct).ConfigureAwait(false);
                break;
            case CreateBoardingStayCommand stay:
                var room = await db.Set<Room>().AsNoTracking().Where(r => r.Id == stay.RoomId || r.Code == stay.RoomId)
                    .Select(r => r.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
                if (room is not null) roomIds.Add(room);
                break;
            case TransferStayRoomCommand transfer:
                await AddCurrentRoomAsync(transfer.StayId, roomIds, ct).ConfigureAwait(false);
                if (transfer.TargetRoomId is not null)
                {
                    var target = await db.Set<Room>().AsNoTracking().Where(r => r.Id == transfer.TargetRoomId || r.Code == transfer.TargetRoomId)
                        .Select(r => r.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
                    if (target is not null) roomIds.Add(target);
                }
                else
                {
                    // Auto-transfer selects from the same bounded room projection used by the handler.
                    var candidates = await db.Set<Room>().AsNoTracking().OrderBy(r => r.Code).ThenBy(r => r.Id)
                        .Select(r => r.Id).Take(200).ToListAsync(ct).ConfigureAwait(false);
                    roomIds.UnionWith(candidates);
                }
                break;
            case CheckoutStayCommand checkout:
                await AddCurrentRoomAsync(checkout.StayId, roomIds, ct).ConfigureAwait(false); break;
            case UpdateRoomCommand update:
                roomIds.Add(update.Id); break;
        }
        if (doctor is not null)
        {
            var membership = await db.StaffMemberships.Where(m => m.UserId == doctor && m.Role == StaffRole.Doctor
                && m.Status == MembershipStatus.Active).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (membership is not null) db.Entry(membership).Property(m => m.WorkStartMinute).IsModified = true;
        }
        foreach (var id in roomIds.Order(StringComparer.Ordinal))
        {
            var room = await db.Set<Room>().FirstOrDefaultAsync(r => r.Id == id, ct).ConfigureAwait(false);
            if (room is not null) db.Entry(room).Property(r => r.Capacity).IsModified = true;
        }
        if (db.ChangeTracker.HasChanges())
        {
            try { await db.SaveChangesAsync(ct).ConfigureAwait(false); }
            catch (DbUpdateConcurrencyException ex) { throw new ReservationConcurrencyException(ex); }
        }
        return Result.Ok();
    }
    private async Task AddCurrentRoomAsync(string stayId, HashSet<string> ids, CancellationToken ct)
    {
        var id = await db.Set<BoardingStay>().AsNoTracking().Where(s => s.Id == stayId).Select(s => s.RoomId)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (id is not null) ids.Add(id);
    }
}
