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
    }

    private sealed class FixedClock(DateTimeOffset at) : IClock
    {
        public DateTimeOffset UtcNow => at;
    }
}
