using Animora.Application.UseCases.Dashboard;
using Animora.Contracts.Navigation;
using Animora.Domain.Sales;
using Animora.Domain.Scheduling;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Animora.Domain.Patients;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Queries;

/// <summary>Seven sequential indexed reads per snapshot; the version probe adds one (budget: eight).</summary>
public sealed class DashboardQueries(AnimoraLocalDbContext db) : IDashboardQueries
{
    public async Task<string> VersionAsync(string tenantId, CancellationToken ct) =>
        await db.Set<ActivityEvent>().AsNoTracking().Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.Id).Select(x => x.Id).FirstOrDefaultAsync(ct) ?? string.Empty;

    public async Task<DashboardSlice> GetSliceAsync(string tenantId, int today, string userId, UserRole role, CancellationToken ct)
    {
        var date = new DateTime(today / 10000, today / 100 % 100, today % 100).AddDays(-13);
        var start = date.Year * 10000 + date.Month * 100 + date.Day;
        var facts = await db.Set<DailyFinanceFact>().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.LocalDay >= start && x.LocalDay <= today)
            .OrderBy(x => x.LocalDay)
            .Select(x => new DashboardDay(x.LocalDay, x.RevenueMinor)).ToListAsync(ct);
        if (facts.Count == 0 || facts[^1].LocalDay != today) facts.Add(new DashboardDay(today, 0));
        var feed = db.Set<ActivityEvent>().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.LocalDay == today);
        if (role == UserRole.Doctor) feed = feed.Where(x => x.ActorUserId == userId);
        var activity = await feed.OrderByDescending(x => x.Id).Take(80)
            .Select(x => new DashboardActivity(x.Kind, x.Title, x.EntityId, x.AmountMinor,
                x.AmountSign, x.ActorUserId)).ToListAsync(ct);
        var open = await db.Set<Invoice>().AsNoTracking()
            .CountAsync(x => x.State == InvoiceState.Posted || x.State == InvoiceState.PartiallyPaid, ct);
        var low = await db.Set<StockOnHandSnapshot>().AsNoTracking()
            .CountAsync(x => x.TenantId == tenantId && x.OnHandScaled <= 0, ct);
        var species = await db.Set<Pet>().AsNoTracking().GroupBy(x => x.Species)
            .Select(g => new { Species = g.Key, Count = g.Count() }).ToListAsync(ct);
        var startUtc = new DateTimeOffset(today / 10000, today / 100 % 100, today % 100, 0, 0, 0, TimeSpan.Zero);
        var endUtc = startUtc.AddDays(1);
        var loads = await db.Set<Appointment>().AsNoTracking()
            .Where(x => x.StartUtc >= startUtc && x.StartUtc < endUtc && x.DoctorUserId != null)
            .GroupBy(x => x.DoctorUserId!)
            .Select(g => new DashboardWorkload(g.Key, g.Count())).ToListAsync(ct);
        var queue = await db.Set<Appointment>().AsNoTracking()
            .CountAsync(x => x.StartUtc >= startUtc && x.StartUtc < endUtc &&
                x.State == AppointmentState.Scheduled && (role != UserRole.Doctor || x.DoctorUserId == userId), ct);
        return new DashboardSlice(facts, activity, loads,
            species.ToDictionary(x => x.Species.ToString(), x => x.Count), open, low, queue);
    }
}
