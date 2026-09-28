using Animora.Domain.Clinical;
using Animora.Domain.Patients;
using Animora.Domain.Time;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Seed;

/// <summary>Optional clinical examples; inserted atomically after patient samples exist.</summary>
public sealed class ClinicalSampleData(AnimoraLocalDbContext db, ILocalTenantContext tenant, IClock clock)
{
    public const string SampleKey = "clinical.sample";

    public bool AlreadySeeded() => db.Set<Visit>().IgnoreQueryFilters().Any();

    public void Populate()
    {
        var pets = db.Set<Pet>().AsNoTracking().OrderBy(p => p.Id).Take(18).ToArray();
        if (pets.Length == 0) return;

        var today = new DateTimeOffset(clock.UtcNow.UtcDateTime.Date, TimeSpan.Zero);
        for (var i = 0; i < Math.Min(10, pets.Length); i++)
        {
            var pet = pets[i];
            var at = today.AddDays(-i - 1);
            var fixedClock = new FixedClock(at);
            var visit = Visit.Open(tenant.TenantId, pet.Id, pet.OwnerId, "U-SEED",
                VisitReason.Checkup, fixedClock, chiefComplaint: "Routine examination");
            visit.Start();
            visit.SetVitals(new PetVitals(3m + i, 38.2m, 95 + i, 24 + i, null, null));
            visit.SetAssessment("Routine examination", "Monitor and follow up", "No acute findings");
            visit.Complete(fixedClock);
            visit.MarkReadyForInvoice();
            db.Add(visit);
            db.Add(new MedicalHistoryEntry(tenant.TenantId, pet.Id, MedicalHistoryKind.Visit,
                "Routine examination", at, "U-SEED", fixedClock, "Monitor and follow up", visit.Id, "Visit"));
        }

        // Relative due dates exercise every read-projection bucket without changing domain StateAt().
        var offsets = new[] { -8, -2, 0, 7, 14, 15, 30, 60, 61, 120, -1, 10, 90, 20 };
        for (var i = 0; i < offsets.Length; i++)
        {
            var pet = pets[i % pets.Length];
            const string code = "RABIES";
            var administered = today.AddDays(offsets[i] - VaccineSchedule.IntervalFor(code, 1));
            var record = VaccinationRecord.Administer(tenant.TenantId, pet.Id, code, "Rabies", 1,
                administered, "U-SEED", new FixedClock(administered), totalDoses: 1);
            if (record.IsFailure) throw new InvalidOperationException(record.Error.Code);
            db.Add(record.Value!);
            if (i == offsets.Length - 1)
                db.Add(new MedicalHistoryEntry(tenant.TenantId, pet.Id, MedicalHistoryKind.Vaccination,
                    "Dose administered", today, "U-SEED", new FixedClock(today), "Rabies",
                    record.Value!.Id, "VaccinationRecord"));
        }
    }

    private sealed class FixedClock(DateTimeOffset at) : IClock
    {
        public DateTimeOffset UtcNow => at;
    }
}
