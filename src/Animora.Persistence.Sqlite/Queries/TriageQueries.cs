using Animora.Application.Ports;
using Animora.Application.Text;
using Animora.Application.UseCases.Visits.ReadModels;
using Animora.Domain.Clinical;
using Animora.Domain.Identity;
using Animora.Domain.Patients;
using Animora.Domain.Scheduling;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Queries;

/// <summary>Bounded, tenant-filtered triage projections; doctor counts are computed in one SQL query.</summary>
public sealed class TriageQueries(AnimoraLocalDbContext db) : ITriageQueries
{
    public async Task<IReadOnlyList<DoctorRailReadModel>> GetDoctorRailAsync(
        DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken ct)
    {
        var doctors = from membership in db.Set<StaffMembership>().AsNoTracking()
                      where membership.Role == StaffRole.Doctor && membership.Status == MembershipStatus.Active
                      join user in db.Set<User>().AsNoTracking() on membership.UserId equals user.Id
                      where membership.EndedUtc == null && user.Status != UserStatus.Disabled
                      orderby user.DisplayName, user.Id
                      select new DoctorRailReadModel(user.Id, user.DisplayName, string.Empty,
                          // The rail metric must equal the number of rows the booked list can actually show
                          // for this doctor today, so it uses the exact same "active/bookable" filter as
                          // GetBookedForDoctorAsync below (Completed is excluded, like NoShow/Cancelled).
                          // Otherwise a doctor whose today's appointments are all Completed showed a non-zero
                          // count over an empty "نوبت‌های رزرو شده" list (V: count/list divergence).
                          db.Set<Appointment>().Count(a => a.DoctorUserId == user.Id
                              && a.StartUtc >= startUtc && a.StartUtc < endUtc
                              && a.State != AppointmentState.Cancelled && a.State != AppointmentState.NoShow
                              && a.State != AppointmentState.Completed),
                          db.Set<Visit>().Any(v => v.DoctorUserId == user.Id && v.State == VisitState.InProgress),
                          null);
        return await doctors.ToListAsync(ct);
    }

    public async Task<IReadOnlyList<BookedAppointmentReadModel>> GetBookedForDoctorAsync(
        string doctorId, DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken ct)
    {
        // V04: the previous shape left-joined ReceptionQueueEntry (GroupJoin + DefaultIfEmpty) and
        // then applied two further inner joins + a projection, which EF Core SQLite can fail to
        // translate (crash on ToListAsync for doctors that actually have booked rows). Restructured to
        // two inner joins for pet/owner and a pair of correlated subqueries for the optional queue row,
        // so the whole statement translates cleanly.
        // T2: only rows StartVisitFromTicket can actually start (NoShow/Completed are rejected with
        // TICKET_STATE_INVALID), mirroring the doctor-rail query above.
        var booked = from appointment in db.Set<Appointment>().AsNoTracking()
                     where appointment.DoctorUserId == doctorId
                         && appointment.StartUtc >= startUtc && appointment.StartUtc < endUtc
                         && appointment.State != AppointmentState.Cancelled
                         && appointment.State != AppointmentState.NoShow
                         && appointment.State != AppointmentState.Completed
                     join pet in db.Set<Pet>().AsNoTracking() on appointment.PetId equals pet.Id
                     join owner in db.Set<Owner>().AsNoTracking() on appointment.OwnerId equals owner.Id
                     orderby appointment.StartUtc, appointment.Id
                     select new BookedAppointmentReadModel("apt:" + appointment.Id,
                         appointment.StartUtc.Hour * 60 + appointment.StartUtc.Minute,
                         appointment.DurationMinutes, pet.Id, pet.Name, pet.Species.ToString(),
                         owner.FullName, owner.PhonePrimary, appointment.AppointmentTypeCode,
                         db.Set<ReceptionQueueEntry>().Any(q => q.AppointmentId == appointment.Id && q.Priority > 0),
                         db.Set<ReceptionQueueEntry>().Where(q => q.AppointmentId == appointment.Id)
                             .Select(q => q.State.ToString()).FirstOrDefault() ?? "Waiting");
        return await booked.ToListAsync(ct);
    }

    public async Task<IReadOnlyList<TriagePatientReadModel>> GetRecentPatientsAsync(string? search, CancellationToken ct)
    {
        var pets = db.Set<Pet>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = PersianText.Normalize(search);
            pets = pets.Where(p => EF.Functions.Like(p.SearchKey, $"%{term}%"));
        }
        var recent = from pet in pets
                     join owner in db.Set<Owner>().AsNoTracking() on pet.OwnerId equals owner.Id
                     orderby db.Set<Visit>().Where(v => v.PetId == pet.Id)
                         .Max(v => (DateTimeOffset?)v.StartedUtc) descending, pet.Id
                     select new TriagePatientReadModel(pet.Id, pet.Name, pet.Species.ToString(),
                         owner.Id, owner.FullName, owner.PhonePrimary);
        return await recent.Take(50).ToListAsync(ct);
    }
}

/// <summary>Credential-free server directory refresh, enlisted in the dispatcher command transaction.</summary>
public sealed class VisitDoctorDirectoryWriter(AnimoraLocalDbContext db, IClock clock) : IVisitDoctorDirectoryWriter
{
    public async Task<Result> RefreshAsync(IReadOnlyList<VisitDoctorDirectoryEntry> doctors,
        string actorUserId, CancellationToken ct)
    {
        // Validate the WHOLE roster before changing anything; an incomplete/malformed response must not
        // remove every doctor. The command pipeline owns the transaction, including legacy-name retirement.
        if (doctors is null || doctors.Any(d => d is null || string.IsNullOrWhiteSpace(d.UserId)
                || d.UserId.Length > 128 || string.IsNullOrWhiteSpace(d.Username) || d.Username.Length > 128
                || string.IsNullOrWhiteSpace(d.Name) || d.Name.Length > 200 || d.UserId == actorUserId)
            || doctors.Select(d => d.UserId).Distinct(StringComparer.Ordinal).Count() != doctors.Count
            || doctors.Select(d => d.Username.Trim().ToLowerInvariant()).Distinct(StringComparer.Ordinal).Count() != doctors.Count)
            return Result.Fail(ErrorCodes.ValidationFailed);

        // Two bounded clinic-roster reads, not a query per doctor; tenant filters remain enabled.
        var users = await db.Set<User>().AsTracking().ToListAsync(ct);
        var memberships = await db.Set<StaffMembership>().AsTracking().ToListAsync(ct);
        var byId = users.ToDictionary(u => u.Id, StringComparer.Ordinal);
        var byName = users.ToDictionary(u => u.UserName, StringComparer.Ordinal);
        var doctorIds = doctors.Select(d => d.UserId).ToHashSet(StringComparer.Ordinal);
        foreach (var membership in memberships.Where(m => m.Role == StaffRole.Doctor
                     && m.Status == MembershipStatus.Active && !doctorIds.Contains(m.UserId)))
            membership.Suspend(); // Keep historical visits and credentials; never delete an identity.

        foreach (var doctor in doctors)
        {
            if (!byId.TryGetValue(doctor.UserId, out var user))
            {
                var username = doctor.Username.Trim().ToLowerInvariant();
                if (byName.TryGetValue(username, out var legacy))
                {
                    if (legacy.Id == actorUserId || doctorIds.Contains(legacy.Id))
                        return Result.Fail(ErrorCodes.ConcurrencyConflict);
                    // Match online-login mirroring: server ids must win over legacy random local ids.
                    foreach (var membership in memberships.Where(m => m.UserId == legacy.Id))
                        membership.End(clock);
                    legacy.Supersede();
                    await db.SaveChangesAsync(ct); // Free the unique username before inserting its replacement.
                    byName.Remove(username);
                }
                user = User.CreateWithId(doctor.UserId, db.CurrentTenantId, doctor.Username, doctor.Name, clock);
                // No password hash: listing a doctor must NEVER make an account able to sign in locally.
                db.Set<User>().Add(user);
                byId.Add(user.Id, user);
                byName.Add(user.UserName, user);
            }
            user.UpdateDisplayName(doctor.Name);
            var member = memberships.FirstOrDefault(m => m.UserId == doctor.UserId && m.Role == StaffRole.Doctor);
            if (member is null)
            {
                member = StaffMembership.Create(db.CurrentTenantId, user.Id, StaffRole.Doctor, clock);
                db.Set<StaffMembership>().Add(member);
                memberships.Add(member);
            }
            if (doctor.IsActive)
            {
                member.Reinstate();
                if (user.Status == UserStatus.Disabled) user.Enable();
            }
            else
            {
                member.Suspend();
            }
        }
        return Result.Ok();
    }
}
