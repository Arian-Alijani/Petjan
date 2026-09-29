using Animora.Application.UseCases.Profile;
using Animora.Domain.Clinical;
using Animora.Domain.Identity;
using Animora.Domain.Scheduling;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Queries;

/// <summary>
/// C10 profile/settings reads. Every number is counted from a durable, user-scoped table (appointments,
/// visits, vaccinations, the activity feed) — never from sample data — and each read is one scoped,
/// tenant-filtered statement. The C09 finance facts are deliberately not used here: they are clinic-scoped,
/// while every profile KPI is scoped to the signed-in user.
/// </summary>
public sealed class ProfileQueries(AnimoraLocalDbContext db) : IProfileQueries
{
    public async Task<string?> DisplayNameAsync(string userId, CancellationToken ct) =>
        await db.Set<User>().AsNoTracking().Where(u => u.Id == userId)
            .Select(u => u.DisplayName).FirstOrDefaultAsync(ct);

    public async Task<string?> RoleAsync(string tenantId, string userId, CancellationToken ct) =>
        await db.Set<StaffMembership>().AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.UserId == userId && m.Status == MembershipStatus.Active)
            .Select(m => m.Role.ToString()).FirstOrDefaultAsync(ct);

    public async Task<ProfileFacts> FactsAsync(string tenantId, string userId, CancellationToken ct)
    {
        var lastLogin = await db.Set<User>().AsNoTracking().Where(u => u.Id == userId)
            .Select(u => u.LastLoginUtc).FirstOrDefaultAsync(ct);

        var preferences = await db.Set<UserPreference>().AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.UserId == userId)
            .ToDictionaryAsync(p => p.Key, p => p.ValueJson, StringComparer.Ordinal, ct);

        var stats = new List<ProfileStatRow>(4)
        {
            new(UserProfileReadModel.AppointmentsStat, await db.Set<Appointment>().AsNoTracking()
                .CountAsync(a => a.TenantId == tenantId && a.DoctorUserId == userId, ct)),
            new(UserProfileReadModel.VisitsStat, await db.Set<Visit>().AsNoTracking()
                .CountAsync(v => v.TenantId == tenantId && v.DoctorUserId == userId, ct)),
            new(UserProfileReadModel.VaccinationsStat, await db.Set<VaccinationRecord>().AsNoTracking()
                .CountAsync(v => v.TenantId == tenantId && v.AdministeredByUserId == userId, ct)),
            new(UserProfileReadModel.FeedStat, await db.Set<ActivityEvent>().AsNoTracking()
                .CountAsync(a => a.TenantId == tenantId && a.ActorUserId == userId, ct)),
        };

        // Exactly one active local session exists per device (filtered unique index), so the row with the
        // newest activity is this device's current session.
        var sessions = await db.Set<LocalSession>().AsNoTracking()
            .Where(s => s.UserId == userId && s.EndedAtUtc == null)
            .OrderByDescending(s => s.LastActivityUtc)
            .Select(s => new { s.Id, s.DeviceId, s.StartedAtUtc, s.LastActivityUtc })
            .ToListAsync(ct);
        var rows = new List<ProfileSessionRow>(sessions.Count);
        for (var i = 0; i < sessions.Count; i++)
        {
            var s = sessions[i];
            rows.Add(new ProfileSessionRow(s.Id, s.DeviceId, s.StartedAtUtc, s.LastActivityUtc, IsCurrent: i == 0));
        }

        return new ProfileFacts(lastLogin, preferences, stats, rows);
    }

    public async Task<IReadOnlyList<ProfileTeamRowData>> TeamAsync(string tenantId, CancellationToken ct) =>
        await (from m in db.Set<StaffMembership>().AsNoTracking()
               join u in db.Set<User>().AsNoTracking() on m.UserId equals u.Id
               where m.TenantId == tenantId && m.Status == MembershipStatus.Active
               orderby u.DisplayName
               select new ProfileTeamRowData(u.Id, u.DisplayName, m.Role.ToString(),
                   m.WorkStartMinute, m.WorkEndMinute)).ToListAsync(ct);
}
