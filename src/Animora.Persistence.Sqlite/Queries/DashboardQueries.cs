using Animora.Application.UseCases.Dashboard;
using Animora.Contracts.Navigation;
using Animora.Domain.Sales;
using Animora.Domain.Scheduling;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Animora.Domain.Patients;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Queries;

/// <summary>Role-specific sequential reads on one context; at most eight including the version probe.</summary>
public sealed class DashboardQueries(AnimoraLocalDbContext db) : IDashboardQueries
{
    public async Task<string> VersionAsync(string tenantId, CancellationToken ct) =>
        await db.Set<ActivityEvent>().AsNoTracking().Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.Id).Select(x => x.Id).FirstOrDefaultAsync(ct) ?? string.Empty;

    public async Task<DashboardSlice> GetSliceAsync(string tenantId, DashboardDayWindow window,
        string userId, UserRole role, CancellationToken ct)
    {
        var manager = role is UserRole.Manager or UserRole.Receptionist;
        var today = window.LocalDay;
        var date = new DateOnly(today / 10000, today / 100 % 100, today % 100).AddDays(-13);
        IReadOnlyList<DashboardDay> facts = [];
        if (manager)
        {
            var start = date.Year * 10000 + date.Month * 100 + date.Day;
            var stored = await db.Set<DailyFinanceFact>().AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.LocalDay >= start && x.LocalDay <= today)
                .Select(x => new DashboardDay(x.LocalDay, x.RevenueMinor)).ToListAsync(ct);
            var byDay = stored.ToDictionary(x => x.LocalDay, x => x.RevenueMinor);
            var series = new DashboardDay[14];
            for (var i = 0; i < series.Length; i++)
            {
                var day = date.AddDays(i);
                var key = day.Year * 10000 + day.Month * 100 + day.Day;
                series[i] = new DashboardDay(key, byDay.GetValueOrDefault(key));
            }
            facts = series;
        }

        var feed = db.Set<ActivityEvent>().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.LocalDay == today);
        if (role == UserRole.Doctor) feed = feed.Where(x => x.ActorUserId == userId);
        // KPIs must describe the whole day, not just the 80-row preview. One aggregate SQL statement.
        var totals = await feed.GroupBy(x => 1).Select(g => new DashboardActivityTotals(
            g.Sum(x => x.Title == "history.payment.registered" ? x.AmountMinor : 0L),
            g.Count(x => x.Title == "history.visit.completed"),
            g.Count(x => x.Title == "history.vaccine.administered"),
            g.Count(x => x.Kind == "Cheque"))).SingleOrDefaultAsync(ct)
            ?? new DashboardActivityTotals(0, 0, 0, 0);
        var activity = await feed.OrderByDescending(x => x.Id).Take(80)
            .Select(x => new DashboardActivity(x.Kind, x.Title, x.EntityId, x.AmountMinor,
                x.AmountSign, x.ActorUserId)).ToListAsync(ct);

        var open = role == UserRole.Doctor ? 0 : await db.Set<Invoice>().AsNoTracking()
            .CountAsync(x => x.State == InvoiceState.Posted || x.State == InvoiceState.PartiallyPaid, ct);
        var low = manager ? await db.Set<StockOnHandSnapshot>().AsNoTracking()
            .CountAsync(x => x.TenantId == tenantId && x.OnHandScaled <= 0, ct) : 0;
        IReadOnlyDictionary<string, int> species = new Dictionary<string, int>();
        if (manager)
        {
            var groups = await db.Set<Pet>().AsNoTracking().Where(x => x.Status == PetStatus.Active)
                .GroupBy(x => x.Species).Select(g => new { Species = g.Key, Count = g.Count() }).ToListAsync(ct);
            species = groups.ToDictionary(x => x.Species.ToString(), x => x.Count);
        }

        var appointments = db.Set<Appointment>().AsNoTracking()
            .Where(x => x.StartUtc >= window.StartUtc && x.StartUtc < window.EndUtc);
        if (role == UserRole.Doctor) appointments = appointments.Where(x => x.DoctorUserId == userId);
        // Queue totals and manager workloads share one indexed appointment read, including unassigned slots.
        var appointmentCounts = await appointments.GroupBy(x => x.DoctorUserId)
            .Select(g => new { DoctorId = g.Key, Total = g.Count(),
                Scheduled = g.Count(x => x.State == AppointmentState.Scheduled) }).ToListAsync(ct);
        IReadOnlyList<DashboardWorkload> loads = manager
            ? appointmentCounts.Where(x => x.DoctorId != null).OrderBy(x => x.DoctorId)
                .Select(x => new DashboardWorkload(x.DoctorId!, x.Total)).ToArray()
            : [];
        return new DashboardSlice(facts, activity, loads, species, open, low,
            appointmentCounts.Sum(x => x.Scheduled), totals);
    }
}
