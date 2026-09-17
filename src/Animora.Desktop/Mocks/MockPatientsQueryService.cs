using Animora.Contracts.Patients;
using Animora.Desktop.Localization;

namespace Animora.Desktop.Mocks;

/// <summary>
/// Deterministic in-memory sample directory for the Patients &amp; Owners
/// screen and its drill-in detail views (Phase 09). Data is re-derived from what
/// the reference's list/grid/pet-file/owner-sheet render (species spread across
/// dog/cat/bird/rabbit/rodent, owners with children, tags, balances, full pet
/// clinical records) — it is NOT copied from Animora-UI's demo dataset.
///
/// Stateful: owners/pets created or edited through the Owner Sheet, Pet File and
/// add wizard persist for the app session, so navigation shows the mutations.
/// </summary>
public sealed class MockPatientsQueryService : IPatientsQueryService
{
    // Sample dates are generated relative to the current day so the
    // directory's relative captions (امروز / N روز پیش / N روز دیگر) read
    // exactly as the reference's fixed-"today" demo does.
    private static DateTime Today => DateTime.Today;

    private readonly List<PetSummary> _pets;
    private readonly List<OwnerSummary> _owners;
    private readonly Dictionary<string, PetDetail> _petDetails = new();

    private int _petSeq;
    private int _ownerSeq;

    public MockPatientsQueryService()
    {
        _owners =
        [
            new("ow-1", "۷۲-۱۰۱۲", "نگار", "محمدی", OwnerGender.Female,
                "09121234567", "09301234567", "02188776655", "0079384512",
                "تهران، سعادت‌آباد، خیابان علامه شمالی، کوچه ۱۸ شرقی، پلاک ۲۴، واحد ۵",
                "اینستاگرام", new DateTime(2021, 5, 4), PatientAvatarTone.A, 0, ["مشتری وفادار"]),
            new("ow-2", "۷۲-۱۰۳۴", "امیر", "رستگار", OwnerGender.Male,
                "09124445566", null, "02144332211", "0064128877",
                "تهران، شهرک غرب، بلوار دادمان، مجتمع آسمان، بلوک ب، طبقه ۷",
                "معرفی دوستان", new DateTime(2021, 1, 23), PatientAvatarTone.B, 1_250_000, ["بدهکار"]),
            new("ow-3", "۷۲-۱۰۵۸", "سارا", "یوسفی", OwnerGender.Female,
                "09357778899", "09197778899", "02166554433", "0018765432",
                "کرج، عظیمیه، میدان مهران، خیابان گلستان، پلاک ۹",
                "جستجوی گوگل", new DateTime(2022, 10, 14), PatientAvatarTone.C, 0, []),
            new("ow-4", "۷۲-۱۰۷۷", "علی", "نوروزی", OwnerGender.Male,
                "09192223344", null, null, "0092233445",
                "تهران، پاسداران، گلستان پنجم، پلاک ۱۲",
                "پت‌شاپ همکار", new DateTime(2023, 4, 8), PatientAvatarTone.D, 0, []),
            new("ow-5", "۷۲-۱۰۹۱", "آرمان", "توسلی", OwnerGender.Male,
                "09333334455", "09121119988", "02122119933", "0037788991",
                "تهران، نیاوران، خیابان جمشیدیه، کوچه یاس، پلاک ۳",
                "دامپزشک ارجاع‌دهنده", new DateTime(2019, 8, 21), PatientAvatarTone.E, 0, ["VIP"]),
            new("ow-6", "۷۲-۱۱۰۴", "مهسا", "کریمی", OwnerGender.Female,
                "09025556677", null, "02133445566", "0055443322",
                "تهران، تهرانپارس، فلکه دوم، خیابان ۱۹۶ غربی، پلاک ۴۱",
                "تابلوی کلینیک", new DateTime(2024, 5, 28), PatientAvatarTone.F, 0, []),
        ];
        _ownerSeq = _owners.Count;

        _pets =
        [
            Pet("pt-1", "۷۲-۱۳۸۶", "پشمک", PetSpecies.Cat, "پرشین", "سفید کرم", PetGender.Female,
                PatientAvatarTone.A, PetStatus.Healthy, "ow-1", new DateTime(2022, 7, 12),
                4.4, 0, "واکسیناسیون", true, "واکسن سه‌گانه (FVRCP)", 365, "985141002317654"),
            Pet("pt-2", "۷۲-۱۴۱۰", "نارنجی", PetSpecies.Cat, "مو کوتاه اهلی", "نارنجی تبی", PetGender.Male,
                PatientAvatarTone.C, PetStatus.FollowUp, "ow-1", new DateTime(2023, 3, 2),
                4.3, -12, "معاینه دوره‌ای", true, "واکسن چهارگانه", 120, null),
            Pet("pt-3", "۷۲-۱۳۸۹", "لئو", PetSpecies.Dog, "گلدن رتریور", "طلایی", PetGender.Male,
                PatientAvatarTone.B, PetStatus.FollowUp, "ow-2", new DateTime(2020, 9, 17),
                33.2, -5, "درمانی", true, "واکسن هشت‌گانه", 45, "985141002319001"),
            Pet("pt-4", "۷۲-۱۴۲۲", "روبی", PetSpecies.Dog, "پامرانین", "کرم", PetGender.Female,
                PatientAvatarTone.F, PetStatus.Healthy, "ow-2", new DateTime(2024, 2, 8),
                3.1, -40, "پوست و مو", true, "واکسن چهارگانه (DHPP)", 210, null),
            Pet("pt-5", "۷۲-۱۳۹۱", "آبی", PetSpecies.Bird, "طوطی کاسکو", "طوسی", PetGender.Female,
                PatientAvatarTone.C, PetStatus.Healthy, "ow-3", new DateTime(2021, 6, 25),
                0.46, -20, "معاینه دوره‌ای", true, "نیوکاسل", 90, null),
            Pet("pt-6", "۷۲-۱۳۹۵", "برفی", PetSpecies.Rabbit, "لوپ هلندی", "سفید خال‌خالی", PetGender.Male,
                PatientAvatarTone.D, PetStatus.FollowUp, "ow-4", new DateTime(2023, 8, 30),
                1.9, -3, "دندان‌پزشکی", false, null, null, null),
            Pet("pt-7", "۷۲-۱۴۰۲", "میلو", PetSpecies.Dog, "شیتزو", "قهوه‌ای روشن", PetGender.Male,
                PatientAvatarTone.E, PetStatus.Critical, "ow-5", new DateTime(2017, 11, 11),
                6.0, -1, "درمانی", true, "واکسن هشت‌گانه", -8, "985141002320077"),
            Pet("pt-8", "۷۲-۱۴۳۱", "کوکو", PetSpecies.Rodent, "خوکچه هندی", "سه‌رنگ", PetGender.Female,
                PatientAvatarTone.A, PetStatus.Healthy, "ow-6", new DateTime(2024, 4, 19),
                0.71, -14, "معاینه دوره‌ای", true, "ضد انگل عمومی", 150, null),
            Pet("pt-9", "۷۲-۱۳۹۸", "زیتون", PetSpecies.Bird, "مرغ عشق", "سبز", PetGender.Unknown,
                PatientAvatarTone.B, PetStatus.Healthy, "ow-3", new DateTime(2023, 1, 6),
                0.05, -55, "واکسیناسیون", false, null, null, null),
            Pet("pt-10", "۷۲-۱۴۴۰", "شیرین", PetSpecies.Cat, "اسکاتیش فولد", "خاکستری", PetGender.Female,
                PatientAvatarTone.E, PetStatus.Healthy, "ow-6", new DateTime(2024, 9, 3),
                2.8, -9, "واکسیناسیون", true, "واکسن سه‌گانه (FVRCP)", 300, null),
        ];
        _petSeq = _pets.Count;

        SeedDetails();
    }

    public Task<IReadOnlyList<PetSummary>> GetPetsAsync() =>
        Task.FromResult<IReadOnlyList<PetSummary>>(_pets);

    public Task<IReadOnlyList<OwnerSummary>> GetOwnersAsync() =>
        Task.FromResult<IReadOnlyList<OwnerSummary>>(_owners);

    public Task<OwnerDetail?> GetOwnerAsync(string id)
    {
        var owner = _owners.FirstOrDefault(o => o.Id == id);
        return Task.FromResult(owner is null ? null : BuildOwnerDetail(owner));
    }

    public Task<PetDetail?> GetPetAsync(string id) =>
        Task.FromResult(_petDetails.TryGetValue(id, out var detail) ? detail : null);

    public Task<OwnerDetail> SaveOwnerAsync(OwnerDetail owner)
    {
        var index = _owners.FindIndex(o => o.Id == owner.Id);
        var summary = new OwnerSummary(
            owner.Id, owner.File, owner.FirstName, owner.LastName, owner.Gender,
            owner.Mobile1, owner.Mobile2, owner.Phone, owner.NationalId, owner.Address,
            owner.Referral, owner.Since, owner.Tone, owner.Balance, owner.Tags);
        if (index >= 0)
        {
            _owners[index] = summary;
        }

        return Task.FromResult(BuildOwnerDetail(summary));
    }

    public Task<PetDetail> SavePetInfoAsync(PetDetail pet)
    {
        _petDetails[pet.Id] = pet;

        // Keep the directory summary (used by Phase 08's cards/table) in sync.
        var index = _pets.FindIndex(p => p.Id == pet.Id);
        if (index >= 0)
        {
            _pets[index] = _pets[index] with
            {
                Name = pet.Name,
                Breed = pet.Breed,
                Color = pet.Color,
                Species = pet.Species,
                Gender = pet.Gender,
                BirthDate = pet.BirthDate,
                CurrentWeightKg = pet.Weights.Count > 0
                    ? pet.Weights.OrderBy(w => w.Date).Last().WeightKg
                    : _pets[index].CurrentWeightKg,
                MicrochipCode = pet.Microchip.Code,
            };
        }

        return Task.FromResult(pet);
    }

    public Task<PetDetail> AddWeightEntryAsync(string petId, WeightEntry entry)
    {
        var detail = _petDetails[petId];
        var weights = detail.Weights.Append(entry).OrderBy(w => w.Date).ToArray();
        var updated = detail with { Weights = weights };
        _petDetails[petId] = updated;

        var index = _pets.FindIndex(p => p.Id == petId);
        if (index >= 0)
        {
            _pets[index] = _pets[index] with { CurrentWeightKg = weights.Last().WeightKg };
        }

        return Task.FromResult(updated);
    }

    public Task<PetDetail> CreatePetAsync(string ownerId, NewPetRequest request)
    {
        var id = $"pt-{++_petSeq + 100}";
        var tone = (PatientAvatarTone)(_pets.Count % 6);
        var detail = BuildNewPetDetail(id, ownerId, request, tone);
        _petDetails[id] = detail;
        _pets.Add(ToSummary(detail));
        return Task.FromResult(detail);
    }

    public Task<OwnerDetail> CreateOwnerAsync(NewOwnerRequest request)
    {
        var id = $"ow-{++_ownerSeq + 100}";
        var file = $"۷۲-{1100 + _ownerSeq}";
        var tone = (PatientAvatarTone)(_owners.Count % 6);
        var owner = new OwnerSummary(
            id, file, request.FirstName, request.LastName, request.Gender,
            request.Mobile1, request.Mobile2, request.Phone, request.NationalId,
            request.Address, request.Referral, Today, tone, 0, []);
        _owners.Add(owner);

        if (request.Pet is { } newPet)
        {
            var petId = $"pt-{++_petSeq + 100}";
            var detail = BuildNewPetDetail(petId, id, newPet, tone);
            _petDetails[petId] = detail;
            _pets.Add(ToSummary(detail));
        }

        return Task.FromResult(BuildOwnerDetail(owner));
    }

    private OwnerDetail BuildOwnerDetail(OwnerSummary owner) =>
        new(owner.Id, owner.File, owner.FirstName, owner.LastName, owner.Gender, owner.Mobile1,
            owner.Mobile2, owner.Phone, owner.NationalId, owner.Address, owner.Referral,
            owner.Since, owner.Tone, owner.Balance, owner.Tags,
            _pets.Where(p => p.OwnerId == owner.Id).ToArray());

    private static PetSummary ToSummary(PetDetail d) =>
        new(d.Id, d.Code, d.Name, d.Species, d.Breed, d.Color, d.Gender, d.Tone, d.Status,
            d.OwnerId, d.BirthDate,
            d.Weights.Count > 0 ? d.Weights.MaxBy(w => w.Date)!.WeightKg : null,
            d.Visits.Count > 0 ? d.Visits.MaxBy(v => v.Date)!.Date : null,
            d.Visits.Count > 0 ? d.Visits.MaxBy(v => v.Date)!.Type : null,
            d.Vaccinations.Count > 0,
            d.Vaccinations.Count > 0 ? d.Vaccinations.MaxBy(v => v.NextDate)!.Name : null,
            d.Vaccinations.Count > 0 ? d.Vaccinations.MaxBy(v => v.NextDate)!.NextDate : null,
            d.Microchip.Code);

    private static PetDetail BuildNewPetDetail(
        string id, string ownerId, NewPetRequest r, PatientAvatarTone tone)
    {
        var birth = r.BirthDate ?? Today;
        var detail = new PetDetail(
            id,
            Code: $"۷۲-{1400 + Random.Shared.Next(1, 400)}",
            r.Name, r.Species, r.Breed, r.Color, r.Gender, tone, PetStatus.Healthy, ownerId,
            birth, r.Habitat, r.Behavior, r.Diet, null, r.Neutered,
            r.Neutered ? Today : null, null,
            new MicrochipInfo(r.MicrochipCode, r.MicrochipDate, null, null),
            new PetVitals(38.5, 120, 24, "۵ از ۹"),
            [], [], [], [], [], [], [], [],
            null);
        return detail;
    }

    // ---- seed details ----

    private void SeedDetails()
    {
        foreach (var pet in _pets)
        {
            _petDetails[pet.Id] = pet.Id == "pt-1" ? RichDetail(pet) : SimpleDetail(pet);
        }
    }

    /// <summary>A fully-populated clinical record (mirrors the reference's pt-1).</summary>
    private static PetDetail RichDetail(PetSummary pet) => new(
        pet.Id, pet.Code, pet.Name, pet.Species, pet.Breed, pet.Color, pet.Gender, pet.Tone,
        pet.Status, pet.OwnerId, pet.BirthDate,
        Habitat: "آپارتمان", Behavior: "آرام و اجتماعی", Diet: "مخلوط خشک و تر",
        BloodType: "A", Neutered: true, NeuteredDate: Jal(1402, 2, 10),
        Insurance: "بیمه پت‌کر — طلایی",
        Microchip: new MicrochipInfo(pet.MicrochipCode, Jal(1401, 9, 2), "Trovan", "بین دو کتف"),
        Vitals: new PetVitals(38.6, 148, 26, "۵ از ۹"),
        Weights:
        [
            new(Jal(1401, 6, 1), 1.4), new(Jal(1401, 9, 2), 2.3), new(Jal(1402, 1, 15), 3.1),
            new(Jal(1402, 6, 20), 3.6), new(Jal(1403, 1, 10), 3.9), new(Jal(1403, 8, 5), 4.2),
            new(Jal(1404, 2, 12), 4.5), new(Jal(1404, 5, 11), 4.4),
        ],
        // Visits / vaccinations / labs / documents are the deliverables of the
        // Visits (10–11) and Vaccination (12) phases; this phase builds the tab
        // UI + empty states but leaves the data to those mocks (phase "Out of
        // scope"). Surgeries/allergies/chronic/notes ARE this phase's — the
        // summary tab renders them.
        Visits: [],
        Vaccinations: [],
        Labs: [],
        Documents: [],
        Surgeries:
        [
            new("عقیم‌سازی (اواریوهیسترکتومی)", Jal(1402, 2, 10), "دکتر آریا نیک",
                "بدون عارضه، بخیه‌ها روز دهم کشیده شد."),
        ],
        Allergies: ["حساسیت به گوشت مرغ"],
        ChronicConditions: [],
        Notes: "در زمان معاینه مضطرب می‌شود؛ استفاده از حوله آرام‌بخش توصیه شده است.");

    /// <summary>A lighter record: identity + microchip + a synthesized weight series.</summary>
    private static PetDetail SimpleDetail(PetSummary pet)
    {
        var (habitat, behavior, diet) = SpeciesProfile(pet.Species);
        var weights = WeightSeries(pet);
        var chronic = pet.Status == PetStatus.Critical
            ? new List<string> { "بیماری مزمن پیشرفته — پایش دوره‌ای" }
            : [];
        var allergies = pet.Status == PetStatus.FollowUp ? new List<string> { "حساسیت فصلی" } : [];

        return new PetDetail(
            pet.Id, pet.Code, pet.Name, pet.Species, pet.Breed, pet.Color, pet.Gender, pet.Tone,
            pet.Status, pet.OwnerId, pet.BirthDate, habitat, behavior, diet,
            pet.Species is PetSpecies.Cat or PetSpecies.Dog ? "A" : null,
            false, null, null,
            new MicrochipInfo(pet.MicrochipCode, Jal(1402, 3, 11), "Datamars", "بین دو کتف"),
            new PetVitals(38.7, 120, 24, "۵ از ۹"),
            weights, [], [], [], [], [], allergies, chronic,
            null);
    }

    private static IReadOnlyList<WeightEntry> WeightSeries(PetSummary pet)
    {
        if (pet.CurrentWeightKg is not { } target)
        {
            return [];
        }

        return
        [
            new(pet.BirthDate.AddMonths(3), Math.Round(target * 0.45, 2)),
            new(pet.BirthDate.AddMonths(9), Math.Round(target * 0.70, 2)),
            new(pet.BirthDate.AddMonths(18), Math.Round(target * 0.88, 2)),
            new(Today, target),
        ];
    }

    private static (string Habitat, string Behavior, string Diet) SpeciesProfile(PetSpecies species) => species switch
    {
        PetSpecies.Cat => ("آپارتمان", "آرام و اجتماعی", "مخلوط خشک و تر"),
        PetSpecies.Dog => ("خانه حیاط‌دار", "بازیگوش", "غذای خشک تجاری"),
        PetSpecies.Bird => ("قفس", "کنجکاو", "دانه و میوه"),
        PetSpecies.Rabbit => ("خانه حیاط‌دار", "ترسو", "غذای خانگی"),
        PetSpecies.Rodent => ("قفس", "آرام و اجتماعی", "دانه و میوه"),
        _ => ("آکواریوم / تراریوم", "کنجکاو", "غذای خانگی"),
    };

    private static DateTime Jal(int year, int month, int day) =>
        new JalaliDate(year, month, day).ToDateTime();

    private static PetSummary Pet(
        string id, string code, string name, PetSpecies species, string breed, string color,
        PetGender gender, PatientAvatarTone tone, PetStatus status, string ownerId, DateTime birth,
        double? weightKg, int lastVisitDaysAgo, string lastVisitType, bool hasVaccines,
        string? nextVaccineName, int? nextVaccineDaysAhead, string? microchip) =>
        new(id, code, name, species, breed, color, gender, tone, status, ownerId, birth, weightKg,
            lastVisitDaysAgo >= 0 ? Today.AddDays(-lastVisitDaysAgo) : null,
            lastVisitType, hasVaccines,
            nextVaccineDaysAhead is { } days ? Today.AddDays(days) : null,
            nextVaccineName,
            microchip);
}
