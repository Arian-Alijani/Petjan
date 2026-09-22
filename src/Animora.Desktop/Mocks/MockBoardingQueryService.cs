using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Animora.Contracts.Boarding;
using Animora.Contracts.Identity;
using Animora.Desktop.Localization;

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
/// one in-memory store.
/// <para>
/// <b>History (R3).</b> Each mutation also appends a
/// <see cref="BoardingHistoryEntry"/> to a self-contained ledger (stamped with
/// the audit <see cref="ActorRef"/> from the injected identity), so
/// <see cref="GetHistoryAsync"/> can serve a paged, filtered, sorted view over
/// the <b>whole</b> set without the UI ever materialising it (PERFORMANCE.md §6).
/// This is the single swap point a future backend replaces (R-18).
/// </para>
/// </summary>
public sealed class MockBoardingQueryService : IBoardingQueryService
{
    private readonly List<BoardingStay> _stays;
    private readonly Dictionary<string, List<BoardingStayItem>> _items = new(StringComparer.Ordinal);

    // O(1) id -> list-index lookup (PERFORMANCE.md §2/§13): kept in sync
    // incrementally (CreateStayAsync inserts at 0 and shifts every existing
    // index, so it rebuilds; UpdateStay replaces in place, index unchanged).
    private readonly Dictionary<string, int> _stayIndexById = new(StringComparer.Ordinal);

    // The audit identity seam (R1/R3): every recorded mutation is stamped from it.
    private readonly ICurrentUserContext _user;

    // Boarding-history ledger (R3). Built lazily into a cached row list and
    // rebuilt only after a mutation, so repeated paging/sorting does not
    // re-derive the rows (PERFORMANCE.md §13).
    private readonly List<BoardingHistoryEntry> _history = [];
    private IReadOnlyList<HistoryRow>? _historyRows;
    private bool _historyDirty = true;

    private static readonly ActorRef[] SeedActors =
    [
        new("u-aria", "دکتر آریا نیک", "مدیر کلینیک", "آ"),
        new("u-mina", "دکتر مینا همتی", "دکتر دامپزشک", "م"),
        new("u-soheil", "دکتر سهیل کاویانی", "دکتر دامپزشک", "س"),
        new("u-sara", "منشی سارا", "پذیرش", "س"),
        new("u-negar", "منشی نگار", "پذیرش", "ن"),
    ];

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

    // Static selectable catalog (bx-*/sv-*/it-* combined) is looked up by ref
    // in AddItemAsync; index it once instead of re-flattening + scanning per call.
    // Declared AFTER the seed arrays it flattens: static field initializers run in
    // textual order, so AllSelectables() must not run before ExtraServiceSeed/
    // ClinicServiceSeed/ClinicItemSeed are assigned (else they are still null).
    private static readonly Dictionary<string, BoardingSelectableItem> SelectablesByRef =
        AllSelectables().ToDictionary(x => x.Ref, StringComparer.Ordinal);

    // Reference knownPets (boarding.js openNewStayModal) — patient suggestions.
    private static readonly BoardingKnownPet[] KnownPetSeed =
    [
        new("میشا", BoardingSpecies.Cat, "پرشین", "نگار محمدی", "۰۹۱۲ ۳۴۵ ۶۷۸۹", "pt-1"),
        new("لئو", BoardingSpecies.Dog, "ژرمن شپرد", "امیر رستگار", "۰۹۳۵ ۱۱۲ ۲۳۳۴", "pt-3"),
        new("هاپو", BoardingSpecies.Dog, "گلدن رتریور", "فاطمه رحیمی", "۰۹۳۵ ۱۰۸ ۵۵۶۶", "pt-10"),
        new("سیمبا", BoardingSpecies.Cat, "مین‌کون", "کیانا فتحی", "۰۹۲۱ ۶۶۴ ۲۲۱۰", "pt-11"),
    ];

    private static readonly string[] Tones = ["a", "b", "c", "d", "e", "f"];

    public MockBoardingQueryService(ICurrentUserContext user)
    {
        _user = user;

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
                ExtraItemsCost: 1_300_000),

            new BoardingStay(
                Id: "bd-2", PetName: "میشا", Species: BoardingSpecies.Cat, Breed: "پرشین",
                OwnerName: "نگار محمدی", OwnerPhone: "۰۹۱۲ ۳۴۵ ۶۷۸۹", PatientId: "pt-1",
                Room: "B-1", CheckIn: Day(-5), CheckOut: Day(0), DailyRate: 550_000,
                PaymentMode: BoardingPaymentMode.Prepaid, Status: BoardingStatus.Active,
                Tone: "a", Transferred: false,
                Note: "داروی قطره چشم روزی دو بار — توسط پرسنل.",
                ExtraItemsCost: 1_020_000),

            new BoardingStay(
                Id: "bd-3", PetName: "گلچین", Species: BoardingSpecies.Bird, Breed: "کاسکو",
                OwnerName: "سارا یوسفی", OwnerPhone: "۰۹۱۹ ۸۷۶ ۵۴۳۲", PatientId: "",
                Room: "B-2", CheckIn: Day(-10), CheckOut: Day(4), DailyRate: 320_000,
                PaymentMode: BoardingPaymentMode.Postpaid, Status: BoardingStatus.Active,
                Tone: "e", Transferred: false,
                Note: "",
                ExtraItemsCost: 150_000),

            new BoardingStay(
                Id: "bd-4", PetName: "کوکو", Species: BoardingSpecies.Rabbit, Breed: "خرگوش لوپ",
                OwnerName: "مریم آذری", OwnerPhone: "۰۹۱۰ ۵۵۶ ۶۷۷۸", PatientId: "",
                Room: "A-1", CheckIn: Day(-8), CheckOut: Day(-2), DailyRate: 380_000,
                PaymentMode: BoardingPaymentMode.Prepaid, Status: BoardingStatus.Done,
                Tone: "f", Transferred: true,
                Note: "تحویل با رضایت کامل.",
                ExtraItemsCost: 450_000),

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
                ExtraItemsCost: 1_350_000),
        ];

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
        SeedHistory();
    }

    // ── History (R3) ────────────────────────────────────────────────────────

    private ActorRef CurrentActor() =>
        new(_user.UserId, _user.Name, _user.RoleLabel, _user.Initials);

    private static string Jalali(DateOnly date) =>
        JalaliCalendar.ToIso(JalaliDate.FromDateTime(date.ToDateTime(TimeOnly.MinValue)));

    private static string NowTime() =>
        PersianNumberFormatter.ToPersianDigits($"{DateTime.Now.Hour:D2}:{DateTime.Now.Minute:D2}");

    /// <summary>Seeds a realistic historic backlog (spread over ~60 days, varied actors).</summary>
    private void SeedHistory()
    {
        var n = 0;
        foreach (var stay in _stays)
        {
            var nights = Math.Max(1, stay.CheckOut.DayNumber - stay.CheckIn.DayNumber);
            var actor = SeedActors[n % SeedActors.Length];

            AddHistory(new BoardingHistoryEntry(
                $"bh-seed-in-{stay.Id}", BoardingHistoryKind.CheckIn, KindLabel(BoardingHistoryKind.CheckIn),
                stay.Id, stay.PetName, stay.Species, stay.OwnerName, stay.Room,
                Jalali(stay.CheckIn), "۰۹:۳۰", nights, 0, 0, "ثبت پذیرش و ورود به اتاق", actor));

            foreach (var item in _items[stay.Id])
            {
                n++;
                var a = SeedActors[n % SeedActors.Length];
                AddHistory(new BoardingHistoryEntry(
                    $"bh-seed-item-{stay.Id}-{item.Ref}", BoardingHistoryKind.ItemAdded, KindLabel(BoardingHistoryKind.ItemAdded),
                    stay.Id, stay.PetName, stay.Species, stay.OwnerName, stay.Room,
                    Jalali(item.At), "۱۱:۰۰", nights, (long)item.Quantity * item.Price, 1, item.Title, a));
            }

            if (stay.Transferred)
            {
                n++;
                AddHistory(new BoardingHistoryEntry(
                    $"bh-seed-tr-{stay.Id}", BoardingHistoryKind.Transferred, KindLabel(BoardingHistoryKind.Transferred),
                    stay.Id, stay.PetName, stay.Species, stay.OwnerName, stay.Room,
                    Jalali(stay.CheckOut), "۱۲:۰۰", nights, 0, 0, "انتقال فاکتور به صندوق", SeedActors[n % SeedActors.Length]));
            }

            if (stay.Status == BoardingStatus.Done)
            {
                n++;
                var total = (long)nights * stay.DailyRate + stay.ExtraItemsCost;
                AddHistory(new BoardingHistoryEntry(
                    $"bh-seed-out-{stay.Id}", BoardingHistoryKind.CheckOut, KindLabel(BoardingHistoryKind.CheckOut),
                    stay.Id, stay.PetName, stay.Species, stay.OwnerName, stay.Room,
                    Jalali(stay.CheckOut), "۱۶:۰۰", nights, total, 1, "تخلیه و تسویه اقامت", SeedActors[n % SeedActors.Length]));
            }

            n++;
        }

        // Extra backlog so paging/sort/date-range are demonstrable (dozens+ rows).
        var today = DateOnly.FromDateTime(DateTime.Today);
        for (var i = 0; i < 40; i++)
        {
            var stay = _stays[i % _stays.Count];
            var date = today.AddDays(-(5 + (i % 60)));
            var nights = Math.Max(1, stay.CheckOut.DayNumber - stay.CheckIn.DayNumber);
            var kind = (i % 3) switch
            {
                0 => BoardingHistoryKind.ItemAdded,
                1 => BoardingHistoryKind.CheckIn,
                _ => BoardingHistoryKind.ItemRemoved,
            };
            var amount = kind == BoardingHistoryKind.ItemAdded ? 150_000 + (i % 5) * 60_000 : 0;
            var sign = kind == BoardingHistoryKind.ItemAdded ? 1 : 0;
            var note = kind switch
            {
                BoardingHistoryKind.ItemAdded => "افزودن مورد مصرفی",
                BoardingHistoryKind.ItemRemoved => "حذف مورد اشتباه",
                _ => "ثبت پذیرش و ورود به اتاق",
            };

            AddHistory(new BoardingHistoryEntry(
                $"bh-extra-{i}", kind, KindLabel(kind), stay.Id, stay.PetName, stay.Species, stay.OwnerName, stay.Room,
                Jalali(date), $"{8 + (i % 11):D2}:{(i % 6) * 10:D2}", nights, amount, sign, note,
                SeedActors[i % SeedActors.Length]));
        }

        _historyDirty = true;
    }

    private void AddHistory(BoardingHistoryEntry entry)
    {
        _history.Add(entry);
        _historyDirty = true;
    }

    private static string KindLabel(BoardingHistoryKind kind) => kind switch
    {
        BoardingHistoryKind.CheckIn => "پذیرش",
        BoardingHistoryKind.ItemAdded => "افزودن مورد",
        BoardingHistoryKind.ItemRemoved => "حذف مورد",
        BoardingHistoryKind.Transferred => "انتقال به صندوق",
        BoardingHistoryKind.CheckOut => "تخلیه",
        _ => "رویداد",
    };

    private IReadOnlyList<HistoryRow> EnsureHistory()
    {
        if (_historyDirty || _historyRows is null)
        {
            _historyRows = _history
                .Select(e => new HistoryRow(e, PersianText.Normalize(
                    $"{e.PetName} {e.OwnerName} {e.RoomLabel} {e.Note} {e.Actor.Name} {e.StayId}")))
                .ToList();
            _historyDirty = false;
        }

        return _historyRows;
    }

    public Task<BoardingHistoryPage> GetHistoryAsync(BoardingHistoryFilter filter)
    {
        var rows = EnsureHistory();
        var kinds = filter.Kinds ?? [];

        var from = string.IsNullOrWhiteSpace(filter.FromDate) ? null : filter.FromDate;
        var to = string.IsNullOrWhiteSpace(filter.ToDate) ? null : filter.ToDate;
        var q = PersianText.Normalize(filter.Search);

        // Filter → sort → page. Totals are computed over the FULL filtered set
        // so the summary bar stays accurate while only a single page is
        // materialised on the UI thread (PERFORMANCE.md §6).
        var matched = new List<HistoryRow>(rows.Count);
        long inflow = 0, outflow = 0;
        foreach (var row in rows)
        {
            var e = row.Entry;
            if (from is not null && string.CompareOrdinal(e.Date, from) < 0)
            {
                continue;
            }

            if (to is not null && string.CompareOrdinal(e.Date, to) > 0)
            {
                continue;
            }

            if (kinds.Count > 0 && !kinds.Contains(e.Kind))
            {
                continue;
            }

            if (filter.Species is { } species && e.Species != species)
            {
                continue;
            }

            if (q.Length > 0 && !PersianText.Contains(row.Haystack, q))
            {
                continue;
            }

            matched.Add(row);
            if (e.AmountSign > 0)
            {
                inflow += e.Amount;
            }
            else if (e.AmountSign < 0)
            {
                outflow += e.Amount;
            }
        }

        matched.Sort(filter.Sort switch
        {
            BoardingHistorySort.Oldest => OldestComparer,
            BoardingHistorySort.AmountDesc => AmountDescComparer,
            BoardingHistorySort.AmountAsc => AmountAscComparer,
            _ => NewestComparer,
        });

        var pageSize = Math.Max(1, filter.PageSize);
        var skip = (Math.Max(1, filter.Page) - 1) * pageSize;
        var take = Math.Min(pageSize, Math.Max(0, matched.Count - skip));

        IReadOnlyList<BoardingHistoryEntry> items = take <= 0
            ? []
            : matched.GetRange(skip, take).Select(r => r.Entry).ToList();

        return Task.FromResult(new BoardingHistoryPage(items, matched.Count, inflow, outflow));
    }

    private static int CompareDate(HistoryRow a, HistoryRow b, bool ascending)
    {
        var byDate = string.CompareOrdinal(a.Entry.Date, b.Entry.Date);
        if (byDate == 0)
        {
            byDate = string.CompareOrdinal(a.Entry.Time, b.Entry.Time);
        }

        if (byDate == 0)
        {
            byDate = string.CompareOrdinal(a.Entry.Id, b.Entry.Id);
        }

        return ascending ? byDate : -byDate;
    }

    private static readonly Comparison<HistoryRow> NewestComparer = static (a, b) => CompareDate(a, b, ascending: false);
    private static readonly Comparison<HistoryRow> OldestComparer = static (a, b) => CompareDate(a, b, ascending: true);
    private static readonly Comparison<HistoryRow> AmountDescComparer = static (a, b) => b.Entry.Amount.CompareTo(a.Entry.Amount);
    private static readonly Comparison<HistoryRow> AmountAscComparer = static (a, b) => a.Entry.Amount.CompareTo(b.Entry.Amount);

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

        var kind = catalog.Source == BoardingItemSource.ClinicItem
            ? BoardingItemKind.Item
            : BoardingItemKind.Service;

        var qty = Math.Max(1, quantity);
        list.Add(new BoardingStayItem(
            catalog.Ref, kind, catalog.Title, qty, catalog.Price,
            DateOnly.FromDateTime(DateTime.Today)));

        RefreshExtraCost(stayId);

        var stay = _stayIndexById.TryGetValue(stayId, out var i) ? _stays[i] : null;
        if (stay is not null)
        {
            var nights = Math.Max(1, stay.CheckOut.DayNumber - stay.CheckIn.DayNumber);
            AddHistory(new BoardingHistoryEntry(
                $"bh-add-{stayId}-{_history.Count}", BoardingHistoryKind.ItemAdded, KindLabel(BoardingHistoryKind.ItemAdded),
                stayId, stay.PetName, stay.Species, stay.OwnerName, stay.Room,
                JalaliCalendar.Today, NowTime(), nights, (long)qty * catalog.Price, 1, catalog.Title, CurrentActor()));
        }

        return Task.CompletedTask;
    }

    public Task RemoveItemAsync(string stayId, int index)
    {
        if (_items.TryGetValue(stayId, out var list) && index >= 0 && index < list.Count)
        {
            var removed = list[index];
            list.RemoveAt(index);
            RefreshExtraCost(stayId);

            var stay = _stayIndexById.TryGetValue(stayId, out var i) ? _stays[i] : null;
            if (stay is not null)
            {
                var nights = Math.Max(1, stay.CheckOut.DayNumber - stay.CheckIn.DayNumber);
                AddHistory(new BoardingHistoryEntry(
                    $"bh-rm-{stayId}-{_history.Count}", BoardingHistoryKind.ItemRemoved, KindLabel(BoardingHistoryKind.ItemRemoved),
                    stayId, stay.PetName, stay.Species, stay.OwnerName, stay.Room,
                    JalaliCalendar.Today, NowTime(), nights, (long)removed.Quantity * removed.Price, -1, removed.Title, CurrentActor()));
            }
        }

        return Task.CompletedTask;
    }

    public Task TransferAsync(string stayId)
    {
        UpdateStay(stayId, stay => stay with { Transferred = true });

        var stay = _stayIndexById.TryGetValue(stayId, out var i) ? _stays[i] : null;
        if (stay is not null)
        {
            var nights = Math.Max(1, stay.CheckOut.DayNumber - stay.CheckIn.DayNumber);
            AddHistory(new BoardingHistoryEntry(
                $"bh-tr-{stayId}-{_history.Count}", BoardingHistoryKind.Transferred, KindLabel(BoardingHistoryKind.Transferred),
                stayId, stay.PetName, stay.Species, stay.OwnerName, stay.Room,
                JalaliCalendar.Today, NowTime(), nights, 0, 0, "انتقال فاکتور به صندوق", CurrentActor()));
        }

        return Task.CompletedTask;
    }

    public Task CheckoutAsync(string stayId, CheckoutRequest request)
    {
        UpdateStay(stayId, stay => stay with { Status = BoardingStatus.Done });

        var stay = _stayIndexById.TryGetValue(stayId, out var i) ? _stays[i] : null;
        if (stay is not null)
        {
            var nights = Math.Max(1, stay.CheckOut.DayNumber - stay.CheckIn.DayNumber);
            var total = (long)nights * stay.DailyRate + stay.ExtraItemsCost;
            AddHistory(new BoardingHistoryEntry(
                $"bh-out-{stayId}-{_history.Count}", BoardingHistoryKind.CheckOut, KindLabel(BoardingHistoryKind.CheckOut),
                stayId, stay.PetName, stay.Species, stay.OwnerName, stay.Room,
                JalaliCalendar.Today, NowTime(), nights, total, 1, "تخلیه و تسویه اقامت", CurrentActor()));
        }

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
            Tone: Tones[(uid - 1) % Tones.Length],
            Transferred: false,
            Note: request.Note,
            ExtraItemsCost: 0);

        _stays.Insert(0, stay);
        _items[stay.Id] = [];
        RebuildStayIndex();

        var nights = Math.Max(1, stay.CheckOut.DayNumber - stay.CheckIn.DayNumber);
        AddHistory(new BoardingHistoryEntry(
            $"bh-in-{stay.Id}", BoardingHistoryKind.CheckIn, KindLabel(BoardingHistoryKind.CheckIn),
            stay.Id, stay.PetName, stay.Species, stay.OwnerName, stay.Room,
            JalaliCalendar.Today, NowTime(), nights, 0, 0, "ثبت پذیرش و ورود به اتاق", CurrentActor()));

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

    /// <summary>One cached boarding-history row with its pre-normalized search haystack.</summary>
    private sealed record HistoryRow(BoardingHistoryEntry Entry, string Haystack);
}
