using System;
using System.Collections.Generic;
using System.Linq;
using Animora.Contracts.Appointments;
using Animora.Desktop.Localization;

namespace Animora.Desktop.Mocks;

/// <summary>
/// Shared, in-memory appointment store backing both
/// <see cref="MockAppointmentsQueryService"/> and
/// <see cref="MockAppointmentBookingService"/> — the single source of truth for
/// the calendar's mutable event set plus the patient pick-list, mirroring the
/// reference's shared <c>APPTS</c> / <c>PETS</c> collections in
/// Animora-UI/assets/appointments.js. The sample set is deterministically
/// generated with the SAME seeded spread the reference uses
/// (<c>seedData()</c>): a mulberry32 PRNG drives 5–10 appointments across each
/// non-Friday day from 18 days back to 24 days ahead, at clinic hours 07:00–22:00.
/// A future backend replaces both mocks with implementations over the same
/// contracts (R-18), so nothing here leaks past the mock layer.
/// </summary>
public sealed class AppointmentStore
{
    private const int DayStart = 7 * 60;   // 07:00
    private const int DayEnd = 22 * 60;    // 22:00

    private static readonly AppointmentDoctor[] DoctorRoster =
    [
        new("d1", "دکتر آریا نیک", "آریا نیک", "داخلی و جراحی", "آ", "#0d9488"),
        new("d2", "دکتر مینا همتی", "مینا همتی", "واکسن و پیشگیری", "م", "#1570cd"),
        new("d3", "دکتر سهیل کاویانی", "سهیل کاویانی", "دندان‌پزشکی", "س", "#8f74d1"),
        new("d4", "نگار راد", "نگار راد", "آرایش و نگهداری", "ن", "#e08a5c"),
    ];

    private static readonly AppointmentType[] TypeCatalog =
    [
        new("visit", "ویزیت عمومی", "visit", "#0d9488", "#e6f4f0", "#125b52", "IconClipboard", 30),
        new("vaccine", "واکسیناسیون", "vaccine", "#1570cd", "#e6eefb", "#2f5299", "IconSyringe", 20),
        new("dental", "دندان‌پزشکی", "dental", "#8f74d1", "#ece6fa", "#5b3ea1", "IconPaw", 60),
        new("surgery", "جراحی", "surgery", "#d92d20", "#fbe6e3", "#96291f", "IconTools", 90),
        new("groom", "آرایش و پیرایش", "groom", "#e08a5c", "#fbead9", "#8a4a1e", "IconScissors", 60),
        new("lab", "آزمایش", "lab", "#2f9fb0", "#e2f2f5", "#1d6672", "IconFlask", 20),
        new("followup", "پیگیری", "followup", "#b54708", "#fbf0d9", "#8a5c14", "IconClock", 20),
    ];

    private static readonly (string Name, string Kind, string Owner, string Phone, string Av, string Ic, string Species)[] PetSeed =
    [
        ("پشمک", "گربه پرشین", "نگار محمدی", "۰۹۱۲۳۴۵۶۷۸۹", "a", "IconCat", "cat"),
        ("لئو", "سگ ژرمن", "امیر رستگار", "۰۹۳۵۱۱۲۲۳۳۴", "b", "IconDog", "dog"),
        ("آبی", "کاسکو", "سارا یوسفی", "۰۹۱۹۸۷۶۵۴۳۲", "c", "IconBird", "bird"),
        ("میلو", "سگ پامرانین", "رضا کریمی", "۰۹۳۰۴۴۵۵۶۶۷", "d", "IconDog", "dog"),
        ("نانا", "گربه اسکاتیش", "الهام صادقی", "۰۹۱۲۷۷۸۸۹۹۰", "e", "IconCat", "cat"),
        ("رکس", "سگ هاسکی", "محسن دهقان", "۰۹۳۶۲۲۳۳۴۴۵", "f", "IconDog", "dog"),
        ("کوکو", "خرگوش لوپ", "مریم آذری", "۰۹۱۰۵۵۶۶۷۷۸", "a", "IconRabbit", "rabbit"),
        ("سیمبا", "گربه بنگال", "حسین مرادی", "۰۹۱۲۳۳۲۲۱۱۰", "b", "IconCat", "cat"),
        ("بلا", "سگ شیتزو", "زهرا اکبری", "۰۹۳۸۹۹۸۸۷۷۶", "d", "IconDog", "dog"),
        ("توتی", "طوطی برزیلی", "کامران فرد", "۰۹۱۴۱۲۳۴۵۶۷", "c", "IconBird", "bird"),
    ];

    private static readonly string[] Notes =
    [
        "مالک از بی‌اشتهایی طی سه روز گذشته گزارش داده است.",
        "ادامه درمان قبلی؛ بررسی پاسخ به آنتی‌بیوتیک.",
        "نوبت دوره‌ای طبق برنامه پیشگیری کلینیک.",
        "حیوان مضطرب است، زمان بیشتری برای معاینه در نظر گرفته شود.",
        "",
        "قبل از مراجعه ۸ ساعت ناشتا بوده است.",
    ];

    private static readonly int[] BaseRate = [12000, 15000, 22000, 45000, 9000, 18000, 8000];

    private readonly object _gate = new();
    private readonly List<AppointmentEvent> _events;
    private readonly List<BookingPatient> _patients;
    private readonly DateOnly _today;
    private int _uid = 1;

    public AppointmentStore()
    {
        _today = DateOnly.FromDateTime(DateTime.Today);
        _events = SeedEvents();
        _patients = PetSeed
            .Select((p, i) => new BookingPatient("pt-" + (i + 1), p.Name, p.Kind, p.Owner, p.Phone, p.Av, p.Species, p.Ic))
            .ToList();
        _uid = _events.Count + 1;
    }

    public static IReadOnlyList<AppointmentDoctor> Doctors => DoctorRoster;

    public static IReadOnlyList<AppointmentType> Types => TypeCatalog;

    public static int DayStartMinutes => DayStart;

    public static int DayEndMinutes => DayEnd;

    public DateOnly Today => _today;

    /// <summary>Every appointment within an inclusive date range, ordered by start.</summary>
    public IReadOnlyList<AppointmentEvent> Range(DateOnly start, DateOnly end)
    {
        lock (_gate)
        {
            return _events
                .Where(a => a.Date >= start && a.Date <= end)
                .OrderBy(a => a.Date).ThenBy(a => a.StartMinutes)
                .ToList();
        }
    }

    public AppointmentEvent? Get(string id)
    {
        lock (_gate)
        {
            return _events.FirstOrDefault(a => a.Id == id);
        }
    }

    public IReadOnlyList<BookingPatient> Patients
    {
        get
        {
            lock (_gate)
            {
                return _patients.ToList();
            }
        }
    }

    public BookingPatient AddPatient(NewPatientRequest request)
    {
        var species = SpeciesFromKind(request.Kind);
        var tone = "abcdef"[_patients.Count % 6].ToString();
        var patient = new BookingPatient(
            "pt-" + (_patients.Count + 1),
            request.Name,
            request.Kind,
            request.OwnerName,
            string.IsNullOrWhiteSpace(request.Phone) ? "—" : request.Phone,
            tone,
            species.Key,
            species.Icon);
        lock (_gate)
        {
            _patients.Add(patient);
        }

        return patient;
    }

    /// <summary>
    /// The candidate start slots for a doctor on a date at a given duration,
    /// mirroring the reference's <c>freeSlots(dateKey, docId, dur)</c>: step 15
    /// minutes when the duration is under 30, else 30.
    /// </summary>
    public IReadOnlyList<TimeSlot> FreeSlots(DateOnly date, string doctorId, int durationMinutes)
    {
        List<AppointmentEvent> busy;
        lock (_gate)
        {
            busy = _events
                .Where(a => a.Date == date && a.DoctorId == doctorId && a.Status != AppointmentStatus.Cancelled)
                .ToList();
        }

        var step = durationMinutes < 30 ? 15 : 30;
        var outSlots = new List<TimeSlot>();
        for (var m = DayStart; m + durationMinutes <= DayEnd; m += step)
        {
            var start = m;
            var end = m + durationMinutes;
            var clash = busy.Any(b => start < b.EndMinutes && b.StartMinutes < end);
            outSlots.Add(new TimeSlot(m, !clash));
        }

        return outSlots;
    }

    /// <summary>
    /// Overlapping appointments for a doctor on a date against a proposed range,
    /// mirroring the reference's <c>conflicts()</c>.
    /// </summary>
    public IReadOnlyList<AppointmentEvent> Conflicts(DateOnly date, string doctorId, int start, int duration, string? exceptId)
    {
        lock (_gate)
        {
            var end = start + duration;
            return _events
                .Where(a => a.Date == date && a.DoctorId == doctorId &&
                            a.Status != AppointmentStatus.Cancelled && a.Id != exceptId &&
                            start < a.EndMinutes && a.StartMinutes < end)
                .OrderBy(a => a.StartMinutes)
                .ToList();
        }
    }

    /// <summary>
    /// Non-cancelled appointment counts per day across an inclusive range (the
    /// mini calendar's per-day dot tally, reference <c>calInner</c>).
    /// </summary>
    public IReadOnlyDictionary<DateOnly, int> DayCounts(DateOnly start, DateOnly end)
    {
        lock (_gate)
        {
            return _events
                .Where(a => a.Date >= start && a.Date <= end && a.Status != AppointmentStatus.Cancelled)
                .GroupBy(a => a.Date)
                .ToDictionary(g => g.Key, g => g.Count());
        }
    }

    /// <summary>The number of non-cancelled appointments a doctor has on a date.</summary>
    public int DoctorBusyCount(string doctorId, DateOnly date)
    {
        lock (_gate)
        {
            return _events.Count(a => a.Date == date && a.DoctorId == doctorId &&
                                      a.Status != AppointmentStatus.Cancelled);
        }
    }

    public AppointmentEvent Create(NewAppointmentRequest request)
    {
        lock (_gate)
        {
            var id = "a" + _uid++;
            var total = Total(request.TypeId, request.DurationMinutes);
            var ev = new AppointmentEvent(
                Id: id,
                Date: request.Date,
                StartMinutes: request.StartMinutes,
                DurationMinutes: request.DurationMinutes,
                TypeId: request.TypeId,
                DoctorId: request.DoctorId,
                PetName: request.PetName,
                PetKind: request.PetKind,
                OwnerName: request.OwnerName,
                OwnerPhone: request.OwnerPhone,
                AvatarTone: request.AvatarTone,
                SpeciesIconKey: request.SpeciesIconKey,
                Status: AppointmentStatus.Confirmed,
                Note: request.Note,
                Code: "AP-" + PersianNumberFormatter.ToPersianDigits(14050000 + _uid * 7),
                Total: total,
                Invoice: AppointmentInvoiceState.None,
                Payment: AppointmentPaymentMethod.None,
                Visit: AppointmentVisitOutcome.None);
            _events.Add(ev);
            return ev;
        }
    }

    public AppointmentEvent Update(string appointmentId, UpdateAppointmentRequest request)
    {
        lock (_gate)
        {
            var index = _events.FindIndex(a => a.Id == appointmentId);
            if (index < 0)
            {
                throw new KeyNotFoundException($"Appointment '{appointmentId}' was not found.");
            }

            var old = _events[index];
            var updated = old with
            {
                Date = request.Date,
                StartMinutes = request.StartMinutes,
                DurationMinutes = request.DurationMinutes,
                TypeId = request.TypeId,
                DoctorId = request.DoctorId,
                PetName = request.PetName,
                PetKind = request.PetKind,
                OwnerName = request.OwnerName,
                OwnerPhone = request.OwnerPhone,
                AvatarTone = request.AvatarTone,
                SpeciesIconKey = request.SpeciesIconKey,
                Note = request.Note,
                Total = Total(request.TypeId, request.DurationMinutes),
            };
            _events[index] = updated;
            return updated;
        }
    }

    public AppointmentEvent SetStatus(string appointmentId, AppointmentStatus status)
    {
        lock (_gate)
        {
            var index = _events.FindIndex(a => a.Id == appointmentId);
            if (index < 0)
            {
                throw new KeyNotFoundException($"Appointment '{appointmentId}' was not found.");
            }

            var updated = _events[index] with { Status = status };
            _events[index] = updated;
            return updated;
        }
    }

    private static int Total(string typeId, int durationMinutes)
    {
        var index = Array.FindIndex(TypeCatalog, t => t.Id == typeId);
        var rate = index >= 0 ? BaseRate[index] : 12000;
        return durationMinutes * rate;
    }

    private static (string Key, string Icon) SpeciesFromKind(string kind)
    {
        if (kind.Contains("گربه", StringComparison.Ordinal))
        {
            return ("cat", "IconCat");
        }

        if (kind.Contains("پرنده", StringComparison.Ordinal) || kind.Contains("طوطی", StringComparison.Ordinal) ||
            kind.Contains("کاسکو", StringComparison.Ordinal))
        {
            return ("bird", "IconBird");
        }

        if (kind.Contains("خرگوش", StringComparison.Ordinal))
        {
            return ("rabbit", "IconRabbit");
        }

        return ("dog", "IconDog");
    }

    // ── deterministic seeding (mirrors the reference's seedData()) ────────────
    private List<AppointmentEvent> SeedEvents()
    {
        var list = new List<AppointmentEvent>();
        var uid = 1;
        var nowMinutes = DateTime.Now.Hour * 60 + DateTime.Now.Minute;

        for (var off = -18; off <= 24; off++)
        {
            var day = _today.AddDays(off);
            if (WeekIndex(day) == 6)
            {
                continue;   // Friday closed
            }

            var r = new Rng(off * 977 + 13);
            var count = 5 + (int)(r.Next() * 6);
            var used = new HashSet<string>();

            for (var i = 0; i < count; i++)
            {
                var t = TypeCatalog[(int)(r.Next() * TypeCatalog.Length)];
                var doc = DoctorRoster[(int)(r.Next() * DoctorRoster.Length)];
                var pet = PetSeed[(int)(r.Next() * PetSeed.Length)];
                var hour = 7 + (int)(r.Next() * 15);
                var start = hour * 60 + (r.Next() > 0.5 ? 30 : 0);
                if (used.Contains(doc.Id + start))
                {
                    start += 30;
                }

                used.Add(doc.Id + start);
                if (start + t.DurationMinutes > DayEnd)
                {
                    continue;
                }

                var status = AppointmentStatus.Confirmed;
                var rr = r.Next();
                if (off < 0)
                {
                    status = rr > 0.12 ? AppointmentStatus.Done : AppointmentStatus.Cancelled;
                }
                else if (off == 0)
                {
                    status = start < nowMinutes ? AppointmentStatus.Done
                        : rr > 0.75 ? AppointmentStatus.Pending : AppointmentStatus.Confirmed;
                }
                else
                {
                    status = rr > 0.72 ? AppointmentStatus.Pending : AppointmentStatus.Confirmed;
                }

                var typeIndex = Array.FindIndex(TypeCatalog, x => x.Id == t.Id);
                var extras = (int)Math.Round(r.Next() * 3) * 5000;
                var total = t.DurationMinutes * BaseRate[typeIndex] + extras;

                var invoice = AppointmentInvoiceState.None;
                var pay = AppointmentPaymentMethod.None;
                var visit = AppointmentVisitOutcome.None;
                if (status == AppointmentStatus.Done)
                {
                    var pr = r.Next();
                    if (pr < 0.72)
                    {
                        invoice = AppointmentInvoiceState.Paid;
                    }
                    else if (pr < 0.90)
                    {
                        invoice = AppointmentInvoiceState.Unpaid;
                    }
                    else
                    {
                        invoice = AppointmentInvoiceState.Partial;
                    }

                    if (invoice != AppointmentInvoiceState.Unpaid)
                    {
                        pay = r.Next() < 0.55
                            ? AppointmentPaymentMethod.Card
                            : r.Next() < 0.5 ? AppointmentPaymentMethod.Cash : AppointmentPaymentMethod.Online;
                    }

                    visit = r.Next() < 0.94 ? AppointmentVisitOutcome.Complete : AppointmentVisitOutcome.NoShow;
                }
                else if (status == AppointmentStatus.Cancelled)
                {
                    visit = AppointmentVisitOutcome.NoShow;
                }

                var id = uid++;
                list.Add(new AppointmentEvent(
                    Id: "a" + id,
                    Date: day,
                    StartMinutes: start,
                    DurationMinutes: t.DurationMinutes,
                    TypeId: t.Id,
                    DoctorId: doc.Id,
                    PetName: pet.Name,
                    PetKind: pet.Kind,
                    OwnerName: pet.Owner,
                    OwnerPhone: pet.Phone,
                    AvatarTone: pet.Av,
                    SpeciesIconKey: pet.Ic,
                    Status: status,
                    Note: Notes[(int)(r.Next() * Notes.Length)],
                    Code: "AP-" + PersianNumberFormatter.ToPersianDigits(14050000 + id * 7),
                    Total: total,
                    Invoice: invoice,
                    Payment: pay,
                    Visit: visit));
            }
        }

        return list;
    }

    /// <summary>Jalali-weekday index: Saturday = 0 .. Friday = 6 (reference's wIdx).</summary>
    internal static int WeekIndex(DateOnly d) => ((int)d.DayOfWeek + 1) % 7;

    /// <summary>mulberry32 PRNG — a faithful port of the reference's <c>rng()</c>.</summary>
    private sealed class Rng
    {
        private uint _t;

        public Rng(int seed) => _t = unchecked((uint)(seed + 0x6d2b79f5));

        public double Next()
        {
            _t = unchecked((_t ^ (_t >> 15)) * (_t | 1u));
            _t = _t ^ (_t + unchecked((_t ^ (_t >> 7)) * (_t | 61u)));
            return (_t ^ (_t >> 14)) / 4294967296.0;
        }
    }
}
