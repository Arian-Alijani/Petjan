using Animora.Application.Ports;
using Animora.Domain.Clinical;
using Animora.Domain.Patients;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Queries;

/// <summary>Tenant-filtered, bounded vaccination reads. Date buckets are calculated after SQL projection.</summary>
public sealed class VaccinationQueries(AnimoraLocalDbContext db) : IVaccinationQueries
{
    public async Task<IReadOnlyList<VaccinationDueRow>> GetDueAsync(DateOnly localDay, CancellationToken ct)
    {
        var now = new DateTimeOffset(localDay.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var rows = await (from v in db.Set<VaccinationRecord>().AsNoTracking()
            join p in db.Set<Pet>().AsNoTracking() on v.PetId equals p.Id
            join o in db.Set<Owner>().AsNoTracking() on p.OwnerId equals o.Id
            where v.State != VaccinationState.Skipped
            orderby v.NextDueUtc, v.Id
            select new { v.Id, v.PetId, PetName = p.Name, Species = p.Species.ToString(),
                OwnerName = o.FullName, OwnerPhone = o.PhonePrimary, v.VaccineCode, v.VaccineName,
                v.DoseNumber, v.AdministeredUtc, v.NextDueUtc, DoctorId = v.AdministeredByUserId,
                v.BatchNumber }).Take(200).ToListAsync(ct);
        // Done is an explicit history event; the domain aggregate represents only administered doses.
        var ids = rows.Select(r => r.Id).ToArray();
        var completed = await db.Set<MedicalHistoryEntry>().AsNoTracking()
            .Where(h => h.Kind == MedicalHistoryKind.Vaccination && h.Title == "Dose administered" &&
                h.SourceEntityId != null && ids.Contains(h.SourceEntityId))
            .Select(h => h.SourceEntityId!).ToListAsync(ct);
        var completedIds = completed.ToHashSet(StringComparer.Ordinal);
        return rows.Select(r => new VaccinationDueRow(r.Id, r.PetId, r.PetName, r.Species,
            r.OwnerName, r.OwnerPhone, r.VaccineCode, r.VaccineName, r.DoseNumber,
            r.AdministeredUtc, r.NextDueUtc, r.DoctorId, r.BatchNumber,
            r.NextDueUtc is null ? VaccinationDueBucket.Done :
                VaccinationThresholds.BucketFor(DateOnly.FromDateTime(r.NextDueUtc.Value.UtcDateTime).DayNumber - localDay.DayNumber,
                    completedIds.Contains(r.Id)))).ToArray();
    }

    public async Task<VaccinationDueRow?> GetRecordAsync(string id, DateOnly localDay, CancellationToken ct) =>
        (await GetDueAsync(localDay, ct)).FirstOrDefault(r => r.Id == id);

    public Task<bool> HasNextDoseAsync(string petId, string vaccineCode, int doseNumber, CancellationToken ct) =>
        db.Set<VaccinationRecord>().AsNoTracking().AnyAsync(r => r.PetId == petId &&
            r.VaccineCode == vaccineCode && r.DoseNumber == doseNumber, ct);

    public async Task<(IReadOnlyList<VaccinationHistoryRow> Items, int Total)> GetHistoryAsync(
        DateTimeOffset? fromUtc, DateTimeOffset? toUtc, string? search, bool oldest,
        int page, int pageSize, CancellationToken ct)
    {
        var query = from h in db.Set<MedicalHistoryEntry>().AsNoTracking()
                    join p in db.Set<Pet>().AsNoTracking() on h.PetId equals p.Id
                    join o in db.Set<Owner>().AsNoTracking() on p.OwnerId equals o.Id
                    where h.Kind == MedicalHistoryKind.Vaccination
                    select new { h, p, o };
        if (fromUtc is { } from) query = query.Where(r => r.h.OccurredUtc >= from);
        if (toUtc is { } to) query = query.Where(r => r.h.OccurredUtc < to);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(r => r.p.Name.Contains(term) || r.o.FullName.Contains(term) ||
                r.h.Title.Contains(term));
        }
        var total = await query.CountAsync(ct);
        var size = Math.Clamp(pageSize, 1, 200);
        var skip = (long)(Math.Max(1, page) - 1) * size;
        if (skip >= total) return ([], total);
        var ordered = oldest ? query.OrderBy(r => r.h.OccurredUtc).ThenBy(r => r.h.Id)
            : query.OrderByDescending(r => r.h.OccurredUtc).ThenBy(r => r.h.Id);
        var rows = await ordered.Skip((int)skip).Take(size).Select(r =>
            new VaccinationHistoryRow(r.h.Id, r.h.SourceEntityId, r.p.Name, r.p.Species.ToString(),
                r.o.FullName, r.h.Title, r.h.Detail, r.h.OccurredUtc, r.h.RecordedByUserId)).ToListAsync(ct);
        return (rows, total);
    }

    public async Task<IReadOnlyList<SmsTemplateReadModel>> GetTemplatesAsync(CancellationToken ct) =>
        await db.Set<SmsTemplateRow>().AsNoTracking().OrderBy(t => t.Id)
            .Select(t => new SmsTemplateReadModel(t.Id, t.Name, t.Channel, t.Body)).ToListAsync(ct);
}
