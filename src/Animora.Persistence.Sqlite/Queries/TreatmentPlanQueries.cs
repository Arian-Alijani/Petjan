using Animora.Application.Ports;
using Animora.Domain.Clinical;
using Animora.Domain.Patients;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Queries;

/// <summary>Tenant-filtered, bounded reads behind the «دارو و واکسن» calendar, patient plan list and clinic catalog.</summary>
public sealed class TreatmentPlanQueries(AnimoraLocalDbContext db) : ITreatmentPlanQueries
{
    public async Task<IReadOnlyList<TreatmentCalendarRow>> GetCalendarAsync(DateOnly from, DateOnly to,
        IReadOnlyCollection<TreatmentKind>? kinds, TreatmentItemType? itemType, string? petId, int take, CancellationToken ct)
    {
        var limit = Math.Clamp(take, 1, 2000);
        var occurrences = db.Set<TreatmentOccurrence>().AsNoTracking()
            .Where(o => o.DueDay >= from && o.DueDay <= to && o.State != TreatmentOccurrenceState.Skipped);
        var plans = db.Set<TreatmentPlan>().AsNoTracking().Where(p => p.State != TreatmentPlanState.Cancelled);
        if (kinds is { Count: > 0 })
        {
            var kindList = kinds.ToList();
            plans = plans.Where(p => kindList.Contains(p.Kind));
        }

        if (itemType is { } type)
        {
            plans = plans.Where(p => p.ItemType == type);
        }

        if (!string.IsNullOrWhiteSpace(petId))
        {
            plans = plans.Where(p => p.PetId == petId);
        }

        var rows = await (from o in occurrences
                join p in plans on o.PlanId equals p.Id
                join pet in db.Set<Pet>().AsNoTracking() on p.PetId equals pet.Id
                join owner in db.Set<Owner>().AsNoTracking() on pet.OwnerId equals owner.Id
                orderby o.DueDay, p.Title, o.Id
                select new
                {
                    OccurrenceId = o.Id, PlanId = p.Id, o.DueDay, PetId = pet.Id, PetName = pet.Name, pet.Species,
                    OwnerName = owner.FullName, p.Title, p.Dosage, p.ItemType, p.Kind, o.State, o.DoneUtc, p.NotifyOwner,
                })
            .Take(limit).ToListAsync(ct);

        return rows.Select(r => new TreatmentCalendarRow(r.OccurrenceId, r.PlanId, r.DueDay, r.PetId, r.PetName,
            r.Species.ToString(), r.OwnerName, r.Title, r.Dosage, r.ItemType, r.Kind, r.State, r.DoneUtc, r.NotifyOwner)).ToArray();
    }

    public async Task<IReadOnlyList<TreatmentPlanRow>> GetPlansForPetAsync(string petId, int take, CancellationToken ct)
    {
        var limit = Math.Clamp(take, 1, 500);
        var plans = await (from p in db.Set<TreatmentPlan>().AsNoTracking()
                join pet in db.Set<Pet>().AsNoTracking() on p.PetId equals pet.Id
                where p.PetId == petId
                orderby p.CreatedUtc descending, p.Id
                select new { Plan = p, PetName = pet.Name })
            .Take(limit).ToListAsync(ct);
        if (plans.Count == 0)
        {
            return [];
        }

        // One bounded aggregate over the listed plans' occurrences (≤ 365 per plan by construction).
        var ids = plans.Select(x => x.Plan.Id).ToList();
        var counters = await db.Set<TreatmentOccurrence>().AsNoTracking()
            .Where(o => ids.Contains(o.PlanId))
            .GroupBy(o => o.PlanId)
            .Select(g => new
            {
                PlanId = g.Key,
                Total = g.Count(),
                Done = g.Count(o => o.State == TreatmentOccurrenceState.Done),
                First = g.Min(o => o.DueDay),
                Last = g.Max(o => o.DueDay),
            })
            .ToListAsync(ct);
        var next = await db.Set<TreatmentOccurrence>().AsNoTracking()
            .Where(o => ids.Contains(o.PlanId) && o.State == TreatmentOccurrenceState.Pending)
            .GroupBy(o => o.PlanId)
            .Select(g => new { PlanId = g.Key, Day = g.Min(o => o.DueDay) })
            .ToListAsync(ct);
        var byPlan = counters.ToDictionary(c => c.PlanId, StringComparer.Ordinal);
        var nextByPlan = next.ToDictionary(c => c.PlanId, c => c.Day, StringComparer.Ordinal);

        return plans.Select(x =>
        {
            var p = x.Plan;
            byPlan.TryGetValue(p.Id, out var c);
            DateOnly? nextDay = nextByPlan.TryGetValue(p.Id, out var d) ? d : null;
            return new TreatmentPlanRow(p.Id, p.PetId, x.PetName, p.ItemType, p.Kind, p.Title, p.VaccineCode, p.Dosage,
                p.Notes, p.NotifyOwner, p.ScheduleType, p.State, c?.Total ?? 0, c?.Done ?? 0,
                c?.First ?? default, c?.Last ?? default, nextDay, p.CreatedUtc);
        }).ToArray();
    }

    public async Task<IReadOnlyList<CustomVaccineRow>> GetCustomCatalogAsync(CancellationToken ct) =>
        (await db.Set<VaccineCatalogItem>().AsNoTracking()
            .Where(v => v.IsActive)
            .OrderBy(v => v.Name).ThenBy(v => v.Id)
            .Take(500)
            .Select(v => new CustomVaccineRow(v.Code, v.Name, v.Protects, v.TotalDoses, v.BoosterDays, v.Stock, v.MinStock))
            .ToListAsync(ct));
}
