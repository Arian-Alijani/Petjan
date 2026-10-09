using Animora.Application.Ports;
using Animora.Domain.Clinical;
using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Repositories;

public sealed class TreatmentPlanRepository(AnimoraServerDbContext db) : TenantRepository<TreatmentPlan>(db), ITreatmentPlanRepository
{
    public Task<TreatmentPlan?> FindPlanAsync(string id, CancellationToken ct = default) =>
        db.Set<TreatmentPlan>().AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    public Task<TreatmentOccurrence?> FindOccurrenceAsync(string id, CancellationToken ct = default) =>
        db.Set<TreatmentOccurrence>().AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<IReadOnlyList<TreatmentOccurrence>> ListPendingOccurrencesAsync(string planId, CancellationToken ct = default) =>
        await db.Set<TreatmentOccurrence>().AsTracking()
            .Where(e => e.PlanId == planId && e.State == TreatmentOccurrenceState.Pending)
            .OrderBy(e => e.SortOrder)
            .Take(TreatmentScheduleExpander.MaxOccurrences)
            .ToListAsync(ct).ConfigureAwait(false);

    public Task<int> CountPendingExceptAsync(string planId, string exceptOccurrenceId, CancellationToken ct = default) =>
        db.Set<TreatmentOccurrence>().AsNoTracking()
            .CountAsync(e => e.PlanId == planId && e.Id != exceptOccurrenceId && e.State == TreatmentOccurrenceState.Pending, ct);

    public void Add(TreatmentPlanDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        db.Set<TreatmentPlan>().Add(draft.Plan);
        db.Set<TreatmentOccurrence>().AddRange(draft.Occurrences);
    }

    public Task<bool> CatalogCodeExistsAsync(string code, CancellationToken ct = default) =>
        db.Set<VaccineCatalogItem>().AsNoTracking().AnyAsync(e => e.Code == code, ct);

    public void AddCatalogItem(VaccineCatalogItem item) => db.Set<VaccineCatalogItem>().Add(item);
}
