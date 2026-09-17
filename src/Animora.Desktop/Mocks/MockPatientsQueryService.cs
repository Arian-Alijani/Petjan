using Animora.Contracts.Patients;

namespace Animora.Desktop.Mocks;

/// <summary>
/// Deterministic in-memory sample directory for the Patients &amp; Owners
/// screen. Data is re-derived from what the reference's list/grid renders
/// (species spread across dog/cat/bird/rabbit/rodent, owners with children,
/// tags, balances) — it is NOT copied from Animora-UI's demo dataset. The
/// reference's fixed "today" (1404/05/11) is reproduced so the relative
/// last-visit / next-vaccine captions read identically.
/// </summary>
public sealed class MockPatientsQueryService : IPatientsQueryService
{
    // Sample dates are generated relative to the current day so the
    // directory's relative captions (امروز / N روز پیش / N روز دیگر) read
    // exactly as the reference's fixed-"today" demo does.
    private static DateTime Today => DateTime.Today;

    private readonly IReadOnlyList<PetSummary> _pets;
    private readonly IReadOnlyList<OwnerSummary> _owners;

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
    }

    public Task<IReadOnlyList<PetSummary>> GetPetsAsync() =>
        Task.FromResult(_pets);

    public Task<IReadOnlyList<OwnerSummary>> GetOwnersAsync() =>
        Task.FromResult(_owners);

    private static PetSummary Pet(
        string id,
        string code,
        string name,
        PetSpecies species,
        string breed,
        string color,
        PetGender gender,
        PatientAvatarTone tone,
        PetStatus status,
        string ownerId,
        DateTime birth,
        double? weightKg,
        int lastVisitDaysAgo,
        string lastVisitType,
        bool hasVaccines,
        string? nextVaccineName,
        int? nextVaccineDaysAhead,
        string? microchip) =>
        new(
            id,
            code,
            name,
            species,
            breed,
            color,
            gender,
            tone,
            status,
            ownerId,
            birth,
            weightKg,
            lastVisitDaysAgo >= 0 ? Today.AddDays(-lastVisitDaysAgo) : null,
            lastVisitType,
            hasVaccines,
            nextVaccineName,
            nextVaccineDaysAhead is { } days ? Today.AddDays(days) : null,
            microchip);
}
