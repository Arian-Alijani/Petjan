using Animora.Contracts.Boarding;

namespace Animora.Desktop.Mocks;

/// <summary>
/// Deterministic in-memory implementation of <see cref="IBoardingQueryService"/>.
/// The six stays are re-derived from what the reference screen visually needs
/// (Animora-UI/assets/boarding.js <c>STAYS</c>) so the rebuilt board renders the
/// same cards, status pills, counts and Persian text. Check-in/check-out dates
/// are expressed relative to the live Jalali "today" — exactly like the
/// reference's <c>day(offset)</c> — so the derived status distribution
/// (اقامت فعلی / تخلیه امروز / تخلیه‌شده) and therefore every tab count matches.
/// The stay list, its consumed lines, the room roster and the service/selectable
/// catalogs all live here (the reference's <c>STAYS</c>/<c>ROOMS</c>/
/// <c>EXTRA_SVCS</c>/<c>CLINIC.SERVICES</c>/<c>CLINIC.ITEMS</c>), and every
/// mutation the detail/transfer/checkout/new-stay modals perform mutates this
/// one in-memory store. This is the single swap point a future backend replaces
/// (R-18) — no ViewModel or View change is required to do so.
/// </summary>
public sealed class MockBoardingQueryService : IBoardingQueryService
{
    private readonly List<BoardingStay> _stays;
    private readonly Dictionary<string, List<BoardingStayItem>> _items = new(StringComparer.Ordinal);

    // O(1) id -> list-index lookup (PERFORMANCE.md §2/§13): kept in sync
    // incrementally (CreateStayAsync inserts at 0 and shifts every existing
    // index, so it rebuilds; UpdateStay replaces in place, index unchanged).
    private readonly Dictionary<string, int> _stayIndexById = new(StringComparer.Ordinal);

    // Static selectable catalog (bx-*/sv-*/it-* combined) is looked up by ref
    // in AddItemAsync; index it once instead of re-flattening + scanning per call.
    private static readonly Dictionary<string, BoardingSelectableItem> SelectablesByRef =
        AllSelectables().ToDictionary(x => x.Ref, StringComparer.Ordinal);

    // Reference ROOMS (Animora-UI/assets/boarding.js) — id/label/type/species/cap.
    private static readonly (string Id, string Label, BoardingRoomType Type, BoardingSpecies Species, int Cap)[] RoomSeed =
    [
        ("A-1", "A-1", BoardingRoomType.Standard, BoardingSpecies.Cat, 1),
        ("A-2", "A-2", BoardingRoomType.Standard, BoardingSpecies.Dog, 1),
        ("A-3", "A-3", BoardingRoomType.Standard, BoardingSpecies.Dog, 1),
        ("B-1", "B-1", BoardingRoomType.Standard, BoardingSpecies.Cat, 1),
        ("B-2", "B-2", BoardingRoomType.Standard, BoardingSpecies.Bird, 1),
        ("VIP", "سوئیت VIP", BoardingRoomType.Vip, BoardingSpecies.Dog, 1),
    ];

    // Reference EXTRA_SVCS (boarding.js) — boarding-specific services.
    private static readonly (string Id, string Title, long Price)[] ExtraServiceSeed =
    [
        ("bx-1", "پیاده‌روی و بازی (هر جلسه)", 150_000),
        ("bx-2", "حمام و خشک‌کردن", 400_000),
        ("bx-3", "آراستگی و کوتاهی مو", 550_000),
        ("bx-4", "اسکراب و درمان پوستی", 350_000),
        ("bx-5", "خدمات دامپزشک (ویزیت در باکس)", 600_000),
    ];

    // Reference CLINIC.SERVICES (clinic-data.js) — the «خدمات کلینیک» optgroup.
    private static readonly (string Id, string Title, long Price)[] ClinicServiceSeed =
    [
        ("sv-1", "ویزیت عمومی", 250_000),
        ("sv-2", "واکسن چهارگانه برونشیت", 380_000),
        ("sv-3", "جراحی عقیم‌سازی", 2_800_000),
        ("sv-4", "دندان‌پزشکی و جرم‌گیری", 1_200_000),
        ("sv-5", "آرایش و پیرایش", 650_000),
        ("sv-6", "آزمایش خون کامل", 800_000),
        ("sv-7", "سونوگرافی شکمی", 1_500_000),
        ("sv-8", "رادیولوژی", 900_000),
        ("sv-9", "ویزیت تخصصی پوست", 350_000),
        ("sv-10", "تزریق و سرم‌تراپی", 450_000),
        ("sv-11", "بستری شبانه‌روزی", 1_000_000),
        ("sv-12", "معاینه پیگیری", 180_000),
    ];

    // Reference CLINIC.ITEMS (clinic-data.js) — the «کالاها» optgroup.
    private static readonly (string Id, string Title, long Price)[] ClinicItemSeed =
    [
        ("it-1", "قرص ضد انگل", 160_000),
        ("it-2", "غذای کنسرو مرغ", 85_000),
        ("it-3", "غذای خشک بالغ سگ (۵ کیلو)", 850_000),
        ("it-4", "شامپوی مخصوص حیوانات", 220_000),
        ("it-5", "مکمل تقویت مفصل", 450_000),
        ("it-6", "آنتی‌بیوتیک خوراکی", 320_000),
        ("it-7", "برس و شانه پاکسازی", 125_000),
        ("it-8", "خاک مخصوص گربه", 280_000),
        ("it-9", "ویتامین مایع", 150_000),
        ("it-10", "قلاده ضدکک", 390_000),
    ];

    // Reference knownPets (boarding.js openNewStayModal) — patient suggestions.
    private static readonly BoardingKnownPet[] KnownPetSeed =
    [
        new("میشا", BoardingSpecies.Cat, "پرشین", "نگار محمدی", "۰۹۱۲ ۳۴۵ ۶۷۸۹", "pt-1"),
        new("لئو", BoardingSpecies.Dog, "ژرمن شپرد", "امیر رستگار", "۰۹۳۵ ۱۱۲ ۲۳۳۴", "pt-3"),
        new("هاپو", BoardingSpecies.Dog, "گلدن رتریور", "فاطمه رحیمی", "۰۹۳۵ ۱۰۸ ۵۵۶۶", "pt-10"),
        new("سیمبا", BoardingSpecies.Cat, "مین‌کون", "کیانا فتحی", "۰۹۲۱ ۶۶۴ ۲۲۱۰", "pt-11"),
    ];

    private static readonly string[] Tones = ["a", "b", "c", "d", "e", "f"];

    public MockBoardingQueryService()
    {
        // The reference pins "today" (1404/05/11); re-anchor to the live date so
        // the relative check-in/out offsets keep their reference distribution.
        var today = DateOnly.FromDateTime(DateTime.Today);
        DateOnly Day(int offset) => today.AddDays(offset);

        _stays =
        [
            new BoardingStay(
                Id: "bd-1", PetName: "لئو", Species: BoardingSpecies.Dog, Breed: "ژرمن شپرد",
                OwnerName: "امیر رستگار", OwnerPhone: "۰۹۳۵ ۱۱۲ ۲۳۳۴", PatientId: "pt-3",
                Room: "A-2", CheckIn: Day(-3), CheckOut: Day(1), DailyRate: 800_000,
                PaymentMode: BoardingPaymentMode.Postpaid, Status: BoardingStatus.Active,
                Tone: "b", Transferred: false,
                Note: "پیاده‌روی هر صبح؛ حساسیت غذایی به مرغ — فقط غذای ماهی.",
                ExtraItemsCost: 1_300_000), // it-3 (۱×۸۵۰٬۰۰۰) + bx-1 (۳×۱۵۰٬۰۰۰)

            new BoardingStay(
                Id: "bd-2", PetName: "میشا", Species: BoardingSpecies.Cat, Breed: "پرشین",
                OwnerName: "نگار محمدی", OwnerPhone: "۰۹۱۲ ۳۴۵ ۶۷۸۹", PatientId: "pt-1",
                Room: "B-1", CheckIn: Day(-5), CheckOut: Day(0), DailyRate: 550_000,
                PaymentMode: BoardingPaymentMode.Prepaid, Status: BoardingStatus.Active,
                Tone: "a", Transferred: false,
                Note: "داروی قطره چشم روزی دو بار — توسط پرسنل.",
                ExtraItemsCost: 1_020_000), // it-2 (۴×۸۵٬۰۰۰) + it-8 (۱×۲۸۰٬۰۰۰) + bx-2 (۱×۴۰۰٬۰۰۰)

            new BoardingStay(
                Id: "bd-3", PetName: "گلچین", Species: BoardingSpecies.Bird, Breed: "کاسکو",
                OwnerName: "سارا یوسفی", OwnerPhone: "۰۹۱۹ ۸۷۶ ۵۴۳۲", PatientId: "",
                Room: "B-2", CheckIn: Day(-10), CheckOut: Day(4), DailyRate: 320_000,
                PaymentMode: BoardingPaymentMode.Postpaid, Status: BoardingStatus.Active,
                Tone: "e", Transferred: false,
                Note: "",
                ExtraItemsCost: 150_000), // it-9 (۱×۱۵۰٬۰۰۰)

            new BoardingStay(
                Id: "bd-4", PetName: "کوکو", Species: BoardingSpecies.Rabbit, Breed: "خرگوش لوپ",
                OwnerName: "مریم آذری", OwnerPhone: "۰۹۱۰ ۵۵۶ ۶۷۷۸", PatientId: "",
                Room: "A-1", CheckIn: Day(-8), CheckOut: Day(-2), DailyRate: 380_000,
                PaymentMode: BoardingPaymentMode.Prepaid, Status: BoardingStatus.Done,
                Tone: "f", Transferred: true,
                Note: "تحویل با رضایت کامل.",
                ExtraItemsCost: 450_000), // it-5 (۱×۴۵۰٬۰۰۰)

            new BoardingStay(
                Id: "bd-5", PetName: "هاپو", Species: BoardingSpecies.Dog, Breed: "گلدن رتریور",
                OwnerName: "فاطمه رحیمی", OwnerPhone: "۰۹۳۵ ۱۰۸ ۵۵۶۶", PatientId: "pt-10",
                Room: "A-3", CheckIn: Day(-1), CheckOut: Day(5), DailyRate: 900_000,
                PaymentMode: BoardingPaymentMode.Postpaid, Status: BoardingStatus.Active,
                Tone: "c", Transferred: false,
                Note: "حساسیت پوستی — از صابون معمولی استفاده نشود.",
                ExtraItemsCost: 0),

            new BoardingStay(
                Id: "bd-6", PetName: "سیمبا", Species: BoardingSpecies.Cat, Breed: "مین‌کون",
                OwnerName: "کیانا فتحی", OwnerPhone: "۰۹۲۱ ۶۶۴ ۲۲۱۰", PatientId: "pt-11",
                Room: "VIP", CheckIn: Day(-12), CheckOut: Day(-5), DailyRate: 1_200_000,
                PaymentMode: BoardingPaymentMode.Prepaid, Status: BoardingStatus.Done,
                Tone: "d", Transferred: false,
                Note: "",
                ExtraItemsCost: 1_350_000), // bx-3 (۱×۵۵۰٬۰۰۰) + bx-2 (۲×۴۰۰٬۰۰۰)
        ];

        // Reference items[] per stay, in the same insertion order.
        _items["bd-1"] =
        [
            new("it-3", BoardingItemKind.Item, "غذای خشک بالغ سگ (۵ کیلو)", 1, 850_000, Day(-3)),
            new("bx-1", BoardingItemKind.Service, "پیاده‌روی و بازی (هر جلسه)", 3, 150_000, Day(-1)),
        ];
        _items["bd-2"] =
        [
            new("it-2", BoardingItemKind.Item, "غذای کنسرو مرغ", 4, 85_000, Day(-4)),
            new("it-8", BoardingItemKind.Item, "خاک مخصوص گربه", 1, 280_000, Day(-5)),
            new("bx-2", BoardingItemKind.Service, "حمام و خشک‌کردن", 1, 400_000, Day(-1)),
        ];
        _items["bd-3"] =
        [
            new("it-9", BoardingItemKind.Item, "ویتامین مایع", 1, 150_000, Day(-8)),
        ];
        _items["bd-4"] =
        [
            new("it-5", BoardingItemKind.Item, "مکمل تقویت مفصل", 1, 450_000, Day(-7)),
        ];
        _items["bd-5"] = [];
        _items["bd-6"] =
        [
            new("bx-3", BoardingItemKind.Service, "آراستگی و کوتاهی مو", 1, 550_000, Day(-9)),
            new("bx-2", BoardingItemKind.Service, "حمام و خشک‌کردن", 2, 400_000, Day(-6)),
        ];

        RebuildStayIndex();
    }

    private void RebuildStayIndex()
    {
        _stayIndexById.Clear();
        for (var i = 0; i < _stays.Count; i++)
        {
            _stayIndexById[_stays[i].Id] = i;
        }
    }

    public Task<IReadOnlyList<BoardingStay>> GetStaysAsync() =>
        Task.FromResult<IReadOnlyList<BoardingStay>>(_stays.ToList());

    public Task<StayDetail?> GetDetailAsync(string stayId)
    {
        var stay = _stayIndexById.TryGetValue(stayId, out var index) ? _stays[index] : null;
        if (stay is null)
        {
            return Task.FromResult<StayDetail?>(null);
        }

        var items = _items.TryGetValue(stayId, out var list)
            ? list.ToList()
            : [];

        return Task.FromResult<StayDetail?>(new StayDetail(stay, items));
    }

    public Task AddItemAsync(string stayId, string itemRef, int quantity)
    {
        if (!_items.TryGetValue(stayId, out var list))
        {
            list = [];
            _items[stayId] = list;
        }

        var catalog = SelectablesByRef.GetValueOrDefault(itemRef);
        if (catalog is null)
        {
            return Task.CompletedTask;
        }

        // Reference: a boarding-service ('bx') reference is stored as a service
        // line (kind 'svc'); clinic services stay 'svc', clinic goods 'item'.
        var kind = catalog.Source == BoardingItemSource.ClinicItem
            ? BoardingItemKind.Item
            : BoardingItemKind.Service;

        list.Add(new BoardingStayItem(
            catalog.Ref, kind, catalog.Title, Math.Max(1, quantity), catalog.Price,
            DateOnly.FromDateTime(DateTime.Today)));

        RefreshExtraCost(stayId);
        return Task.CompletedTask;
    }

    public Task RemoveItemAsync(string stayId, int index)
    {
        if (_items.TryGetValue(stayId, out var list) && index >= 0 && index < list.Count)
        {
            list.RemoveAt(index);
            RefreshExtraCost(stayId);
        }

        return Task.CompletedTask;
    }

    public Task TransferAsync(string stayId)
    {
        UpdateStay(stayId, stay => stay with { Transferred = true });
        return Task.CompletedTask;
    }

    public Task CheckoutAsync(string stayId, CheckoutRequest request)
    {
        UpdateStay(stayId, stay => stay with { Status = BoardingStatus.Done });
        return Task.CompletedTask;
    }

    public Task<BoardingStay> CreateStayAsync(NewStayRequest request)
    {
        var uid = _stays.Count + 1;
        var stay = new BoardingStay(
            Id: $"bd-{uid}",
            PetName: request.PetName,
            Species: request.Species,
            Breed: request.Breed,
            OwnerName: request.OwnerName,
            OwnerPhone: request.OwnerPhone,
            PatientId: request.PatientId,
            Room: request.Room,
            CheckIn: request.CheckIn,
            CheckOut: request.CheckOut,
            DailyRate: request.DailyRate,
            PaymentMode: request.PaymentMode,
            Status: BoardingStatus.Active,
            // Reference TONES[(uid - 1) % TONES.length].
            Tone: Tones[(uid - 1) % Tones.Length],
            Transferred: false,
            Note: request.Note,
            ExtraItemsCost: 0);

        // Inserting at the front shifts every existing index, so the id index
        // is rebuilt here rather than patched — cheap relative to the insert
        // itself and keeps the lookup correct without an O(n) shift loop.
        _stays.Insert(0, stay);
        _items[stay.Id] = [];
        RebuildStayIndex();

        return Task.FromResult(stay);
    }

    public Task<IReadOnlyList<RoomInfo>> GetRoomsAsync()
    {
        var occupied = _stays
            .Where(s => s.Status == BoardingStatus.Active)
            .Select(s => s.Room)
            .ToHashSet(StringComparer.Ordinal);

        IReadOnlyList<RoomInfo> rooms = RoomSeed
            .Select(r => new RoomInfo(r.Id, r.Label, r.Type, r.Species, r.Cap, occupied.Contains(r.Id)))
            .ToList();

        return Task.FromResult(rooms);
    }

    public Task<IReadOnlyList<ServiceInfo>> GetServicesAsync()
    {
        IReadOnlyList<ServiceInfo> services = ExtraServiceSeed
            .Select(s => new ServiceInfo(s.Id, s.Title, s.Price))
            .ToList();

        return Task.FromResult(services);
    }

    public Task<IReadOnlyList<BoardingSelectableItem>> GetSelectableItemsAsync() =>
        Task.FromResult<IReadOnlyList<BoardingSelectableItem>>(AllSelectables());

    public Task<IReadOnlyList<BoardingKnownPet>> GetKnownPetsAsync() =>
        Task.FromResult<IReadOnlyList<BoardingKnownPet>>(KnownPetSeed.ToList());

    // Reference optgroup order: خدمات پانسیون (bx) → خدمات کلینیک (sv) → کالاها (it).
    private static List<BoardingSelectableItem> AllSelectables() =>
    [
        .. ExtraServiceSeed.Select(s => new BoardingSelectableItem(s.Id, BoardingItemSource.BoardingService, s.Title, s.Price)),
        .. ClinicServiceSeed.Select(s => new BoardingSelectableItem(s.Id, BoardingItemSource.ClinicService, s.Title, s.Price)),
        .. ClinicItemSeed.Select(s => new BoardingSelectableItem(s.Id, BoardingItemSource.ClinicItem, s.Title, s.Price)),
    ];

    // Keeps the pre-aggregated ExtraItemsCost in sync so the board card's total
    // (nights × rate + extra cost) matches the detail modal's line sum.
    private void RefreshExtraCost(string stayId)
    {
        var cost = _items.TryGetValue(stayId, out var list)
            ? list.Sum(i => (long)i.Quantity * i.Price)
            : 0;

        UpdateStay(stayId, stay => stay with { ExtraItemsCost = cost });
    }

    private void UpdateStay(string stayId, Func<BoardingStay, BoardingStay> mutate)
    {
        if (_stayIndexById.TryGetValue(stayId, out var index))
        {
            _stays[index] = mutate(_stays[index]);
        }
    }
}
