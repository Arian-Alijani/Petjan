using Animora.Domain.Patients;
using Animora.Domain.Time;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Seed;

/// <summary>Deterministic first-run sample owners/pets (C01). Ids derive from a fixed seed instant so test
/// assertions are stable. Runs only when LocalStoreOptions.SeedSampleData is true.</summary>
public sealed class PatientsSampleData(AnimoraLocalDbContext db, ILocalTenantContext tenant, IClock clock)
{
    // Marker key so this block is idempotent like the reference seeds.
    public const string SampleKey = "patients.sample";

    /// <summary>The sample block is applied only when no owner exists yet (never overwrites real data).</summary>
    public bool AlreadySeeded() => db.Set<Owner>().IgnoreQueryFilters().Any();

    public void Populate()
    {
        var tenantId = tenant.TenantId;
        var seed = new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);
        var owners = new List<Owner>();
        var names = new[] { "علی رضایی","مریم حسینی","رضا کریمی","زهرا احمدی","حسین موسوی","فاطمه نوری",
            "محمد صادقی","سارا جعفری","امیر قاسمی","نازنین رحیمی","بهرام یزدانی","لیلا کاظمی" };
        for (var i = 0; i < names.Length; i++)
        {
            var o = Owner.Register(tenantId, names[i], $"0912000{i:D4}", new FixedClock(seed.AddMinutes(i)));
            owners.Add(o);
        }
        db.AddRange(owners);

        var petNames = new[] { "پشمالو","رکس","میو","توتو","بلک","سفید","جوجو","لوسی","مکس","بادی","نیکا","شادی",
            "رامبو","کوکو","سیمبا","نالا","اسکار","دیزی" };
        var species = new[] { PetSpecies.Dog, PetSpecies.Cat, PetSpecies.Bird, PetSpecies.Rodent,
            PetSpecies.Reptile, PetSpecies.Other };
        var statuses = new[] { PetStatus.Active, PetStatus.Inactive, PetStatus.Deceased };
        var pets = new List<Pet>();
        for (var i = 0; i < petNames.Length; i++)
        {
            var owner = owners[i % owners.Count];
            var c = new FixedClock(seed.AddHours(i + 1));
            var pet = Pet.Register(tenantId, owner.Id, petNames[i], species[i % species.Length], c,
                PetGender.Male, seed.AddYears(-(i % 5 + 1)), breed: "mixed");
            if ((i % statuses.Length) != 0) pet.SetStatus(statuses[i % statuses.Length]);
            pets.Add(pet);
        }
        db.AddRange(pets);
        // Two weight entries per pet so the pet-file chart has data.
        var weights = new List<WeightEntry>();
        for (var i = 0; i < pets.Count; i++)
        {
            var c = new FixedClock(seed.AddDays(i));
            var w1 = WeightEntry.Record(tenantId, pets[i].Id, 3m + (i % 10), seed.AddMonths(-2), "U-SEED", c);
            var w2 = WeightEntry.Record(tenantId, pets[i].Id, 4m + (i % 10), seed.AddMonths(-1), "U-SEED", c);
            if (w1.IsSuccess) weights.Add(w1.Value!);
            if (w2.IsSuccess) weights.Add(w2.Value!);
        }
        db.AddRange(weights);

        // Two document-metadata rows per pet (phase: "2 documents each"). C01 seeds only the metadata row
        // (Out of scope: real file storage — D12); FileId/hash are deterministic placeholders so test
        // assertions stay stable and no LocalFileStore payload is required.
        var documents = new List<DocumentReference>();
        var kinds = new[] { DocumentKind.Lab, DocumentKind.Imaging };
        for (var i = 0; i < pets.Count; i++)
        {
            var c = new FixedClock(seed.AddDays(i).AddHours(1));
            for (var d = 0; d < 2; d++)
            {
                var doc = DocumentReference.Attach(
                    tenantId,
                    title: $"سند نمونه {d + 1} - {petNames[i]}",
                    kind: kinds[d],
                    fileId: $"FILE-SEED-{i:D2}-{d}",
                    sizeBytes: 1024L * (i + d + 1),
                    contentHashSha256: DeterministicHash(i, d),
                    uploadedByUserId: "U-SEED",
                    clock: c,
                    petId: pets[i].Id,
                    ownerId: pets[i].OwnerId);
                if (doc.IsSuccess) documents.Add(doc.Value!);
            }
        }
        db.AddRange(documents);
    }

    // A stable, well-formed lowercase-hex SHA-256 string (64 chars) derived from the row indices — no payload.
    private static string DeterministicHash(int petIndex, int docIndex)
    {
        var seed = $"animora-c01-sample-doc-{petIndex:D3}-{docIndex}";
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed));
        return Convert.ToHexStringLower(bytes);
    }

    private sealed class FixedClock(DateTimeOffset at) : IClock
    {
        public DateTimeOffset UtcNow => at;
    }
}
