using System;
using System.Collections.Generic;
using System.Linq;
using Animora.Contracts.Appointments;
using Animora.Desktop.Localization;

namespace Animora.Desktop.Mocks;

/// <summary>
/// In-memory implementation of <see cref="IAppointmentsQueryService"/>. The
/// sample set is deterministically generated with the SAME seeded spread the
/// reference uses (Animora-UI/assets/appointments.js <c>seedData()</c>): a
/// mulberry32 PRNG drives 5–10 appointments across each non-Friday day from
/// 18 days back to 24 days ahead, at clinic hours 07:00–22:00. This keeps the
/// mock (the single swap point for a future backend, R-18) reproducible and
/// visually comparable to the reference, while carrying real
/// <see cref="DateOnly"/> + minute values the real backend fills in.
/// </summary>
public sealed class MockAppointmentsQueryService : IAppointmentsQueryService
{
    private const int DayStart = 7 * 60;   // 07:00
    private const int DayEnd = 22 * 60;    // 22:00

    private static readonly AppointmentDoctor[] Doctors =
    [
        new("d1", "دکتر آریا نیک", "آریا نیک", "داخلی و جراحی", "آ", "#0d9488"),
        new("d2", "دکتر مینا همتی", "مینا همتی", "واکسن و پیشگیری", "م", "#1570cd"),
        new("d3", "دکتر سهیل کاویانی", "سهیل کاویانی", "دندان‌پزشکی", "س", "#8f74d1"),
        new("d4", "نگار راد", "نگار راد", "آرایش و نگهداری", "ن", "#e08a5c"),
    ];

    private static readonly AppointmentType[] Types =
    [
        new("visit", "ویزیت عمومی", "visit", "#0d9488", "#e6f4f0", "#125b52", "IconClipboard", 30),
        new("vaccine", "واکسیناسیون", "vaccine", "#1570cd", "#e6eefb", "#2f5299", "IconSyringe", 20),
        new("dental", "دندان‌پزشکی", "dental", "#8f74d1", "#ece6fa", "#5b3ea1", "IconPaw", 60),
        new("surgery", "جراحی", "surgery", "#d92d20", "#fbe6e3", "#96291f", "IconTools", 90),
        new("groom", "آرایش و پیرایش", "groom", "#e08a5c", "#fbead9", "#8a4a1e", "IconScissors", 60),
        new("lab", "آزمایش", "lab", "#2f9fb0", "#e2f2f5", "#1d6672", "IconFlask", 20),
        new("followup", "پیگیری", "followup", "#b54708", "#fbf0d9", "#8a5c14", "IconClock", 20),
    ];

    private static readonly (string Name, string Kind, string Owner, string Phone, string Av, string Ic)[] Pets =
    [
        ("پشمک", "گربه پرشین", "نگار محمدی", "۰۹۱۲۳۴۵۶۷۸۹", "a", "IconCat"),
        ("لئو", "سگ ژرمن", "امیر رستگار", "۰۹۳۵۱۱۲۲۳۳۴", "b", "IconDog"),
        ("آبی", "کاسکو", "سارا یوسفی", "۰۹۱۹۸۷۶۵۴۳۲", "c", "IconBird"),
        ("میلو", "سگ پامرانین", "رضا کریمی", "۰۹۳۰۴۴۵۵۶۶۷", "d", "IconDog"),
        ("نانا", "گربه اسکاتیش", "الهام صادقی", "۰۹۱۲۷۷۸۸۹۹۰", "e", "IconCat"),
        ("رکس", "سگ هاسکی", "محسن دهقان", "۰۹۳۶۲۲۳۳۴۴۵", "f", "IconDog"),
        ("کوکو", "خرگوش لوپ", "مریم آذری", "۰۹۱۰۵۵۶۶۷۷۸", "a", "IconRabbit"),
        ("سیمبا", "گربه بنگال", "حسین مرادی", "۰۹۱۲۳۳۲۲۱۱۰", "b", "IconCat"),
        ("بلا", "سگ شیتزو", "زهرا اکبری", "۰۹۳۸۹۹۸۸۷۷۶", "d", "IconDog"),
        ("توتی", "طوطی برزیلی", "کامران فرد", "۰۹۱۴۱۲۳۴۵۶۷", "c", "IconBird"),
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

    private readonly List<AppointmentEvent> _events;
    private readonly DateOnly _today;

    public MockAppointmentsQueryService()
    {
        _today = DateOnly.FromDateTime(DateTime.Today);
        _events = Seed();
    }

    public Task<IReadOnlyList<AppointmentDoctor>> GetDoctorsAsync() =>
        Task.FromResult<IReadOnlyList<AppointmentDoctor>>(Doctors);

    public Task<IReadOnlyList<AppointmentType>> GetTypesAsync() =>
        Task.FromResult<IReadOnlyList<AppointmentType>>(Types);

    public Task<IReadOnlyList<AppointmentEvent>> GetForRangeAsync(DateOnly start, DateOnly end) =>
        Task.FromResult<IReadOnlyList<AppointmentEvent>>(
            _events.Where(a => a.Date >= start && a.Date <= end).ToList());

    public Task<IReadOnlyList<AppointmentEvent>> GetQueueAsync() =>
        Task.FromResult<IReadOnlyList<AppointmentEvent>>(
            _events.Where(a => a.Date >= _today && a.Date <= _today.AddDays(20)).ToList());

    public Task<IReadOnlyList<AppointmentEvent>> GetHistoryAsync() =>
        Task.FromResult<IReadOnlyList<AppointmentEvent>>(
            _events.Where(a => a.Date <= _today).ToList());

    // ── deterministic seeding (mirrors the reference's seedData()) ────────────
    private List<AppointmentEvent> Seed()
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
                var t = Types[(int)(r.Next() * Types.Length)];
                var doc = Doctors[(int)(r.Next() * Doctors.Length)];
                var pet = Pets[(int)(r.Next() * Pets.Length)];
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

                var typeIndex = Array.FindIndex(Types, x => x.Id == t.Id);
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
