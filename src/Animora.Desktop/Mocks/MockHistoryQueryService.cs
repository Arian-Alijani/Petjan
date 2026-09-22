using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Animora.Contracts.History;
using Animora.Contracts.Identity;
using Animora.Desktop.Localization;
using Animora.Desktop.Pages.History;

namespace Animora.Desktop.Mocks;

/// <summary>
/// In-memory <see cref="IHistoryQueryService"/>: a self-contained synthetic
/// ledger of cross-module events (sale / appointment / patient / vaccine /
/// visit / expense / inventory / cheque / boarding / purchase) spread over the
/// last ~30 days. Per the phase's "Out of scope", this mock does <b>not</b>
/// pull live data from Phases 06–26's mocks — it re-derives its own
/// deterministic sample set from what the History screen visually needs
/// (CONVENTIONS.md §5). It is the single swappable seam a future
/// backend/reporting layer replaces (R-18). Jalali day arithmetic goes through
/// the Phase 04 helpers; the in-memory filter mirrors the reference's
/// aggregate-then-filter pipeline (Animora-UI/assets/history.js).
/// <para>
/// <b>Performance.</b> The query is <b>server-side</b>: the filter + sort +
/// page window are applied here and exactly one bounded page plus the totals
/// crosses the seam (PERFORMANCE.md §6). Every row carries an <see cref="ActorRef"/>
/// (the audit "who") so the ledger shows the performer.
/// </para>
/// </summary>
public sealed class MockHistoryQueryService : IHistoryQueryService
{
    private readonly IReadOnlyList<HistoryEvent> _events;

    public MockHistoryQueryService()
    {
        _events = BuildEvents();
    }

    public Task<HistoryPage> GetPageAsync(HistoryFilter filter)
    {
        var kinds = filter.Kinds ?? [];
        var from = string.IsNullOrWhiteSpace(filter.FromDate) ? null : filter.FromDate;
        var to = string.IsNullOrWhiteSpace(filter.ToDate) ? null : filter.ToDate;
        var search = filter.Search?.Trim() ?? string.Empty;

        // Filter → sort → page. The totals are computed over the FULL filtered
        // set so the summary bar stays accurate while only a single page is
        // materialised on the UI thread (PERFORMANCE.md §6). Ordinal compares
        // are correct because the Jalali dates are fixed-width "yyyy/MM/dd".
        var matched = new List<HistoryEvent>(_events.Count);
        long income = 0, expense = 0;
        foreach (var ev in _events)
        {
            if (from is not null && string.CompareOrdinal(ev.Date, from) < 0)
            {
                continue;
            }

            if (to is not null && string.CompareOrdinal(ev.Date, to) > 0)
            {
                continue;
            }

            if (kinds.Count > 0 && !kinds.Contains(ev.Kind))
            {
                continue;
            }

            if (search.Length > 0
                && !ev.Title.Contains(search, StringComparison.Ordinal)
                && !ev.Detail.Contains(search, StringComparison.Ordinal)
                && !ev.Entity.Contains(search, StringComparison.Ordinal)
                && !ev.Actor.Name.Contains(search, StringComparison.Ordinal))
            {
                continue;
            }

            matched.Add(ev);
            if (ev.AmountSign > 0)
            {
                income += ev.Amount;
            }
            else if (ev.AmountSign < 0)
            {
                expense += ev.Amount;
            }
        }

        matched.Sort(filter.Sort switch
        {
            HistorySort.Oldest => OldestComparer,
            HistorySort.AmountDesc => AmountDescComparer,
            HistorySort.AmountAsc => AmountAscComparer,
            _ => NewestComparer,
        });

        var pageSize = Math.Max(1, filter.PageSize);
        var skip = (Math.Max(1, filter.Page) - 1) * pageSize;
        var take = Math.Min(pageSize, Math.Max(0, matched.Count - skip));

        IReadOnlyList<HistoryEvent> items = take <= 0 ? [] : matched.GetRange(skip, take);

        return Task.FromResult(new HistoryPage(items, matched.Count, income, expense));
    }

    public Task<string> ExportCsvAsync(HistoryFilter filter)
    {
        // Export is the WHOLE filtered set (not one page): reuse the paged
        // query with an unbounded window.
        var all = GetPageAsync(filter with { Page = 1, PageSize = int.MaxValue }).Result.Items;
        var builder = new StringBuilder();

        // The reference's header row (Animora-UI/assets/history.js exportCSV) +
        // the audit "who" column.
        builder.AppendLine(CsvRow("تاریخ", "زمان", "دسته", "رویداد", "توضیح", "موجودیت", "کاربر", "مبلغ", "وضعیت"));

        foreach (var ev in all)
        {
            var amount = ev.AmountSign < 0 ? "-" + ev.Amount : (ev.AmountSign > 0 ? ev.Amount.ToString(CultureInfo.InvariantCulture) : "0");
            builder.AppendLine(CsvRow(
                ev.Date,
                ev.Time,
                HistoryPresentation.KindLabel(ev.Kind),
                ev.Title,
                ev.Detail,
                ev.Entity,
                ev.Actor.Name,
                amount,
                HistoryPresentation.StatusLabel(ev.Status)));
        }

        return Task.FromResult(builder.ToString());

        static string CsvRow(params string[] cells) =>
            string.Join(',', cells.Select(c => "\"" + c.Replace("\"", "\"\"") + "\""));
    }

    // ── Ordering comparers (allocated once, not per query) ──────────────────

    private static int CompareDate(HistoryEvent a, HistoryEvent b, bool ascending)
    {
        var byDate = string.CompareOrdinal(a.Date, b.Date);
        if (byDate == 0)
        {
            byDate = string.CompareOrdinal(a.Time, b.Time);
        }

        if (byDate == 0)
        {
            byDate = string.CompareOrdinal(a.Id, b.Id);
        }

        return ascending ? byDate : -byDate;
    }

    private static readonly Comparison<HistoryEvent> NewestComparer = static (a, b) => CompareDate(a, b, ascending: false);
    private static readonly Comparison<HistoryEvent> OldestComparer = static (a, b) => CompareDate(a, b, ascending: true);
    private static readonly Comparison<HistoryEvent> AmountDescComparer = static (a, b) => b.Amount.CompareTo(a.Amount);
    private static readonly Comparison<HistoryEvent> AmountAscComparer = static (a, b) => a.Amount.CompareTo(b.Amount);

    // ── Synthetic seed data ─────────────────────────────────────────────────

    private static IReadOnlyList<HistoryEvent> BuildEvents()
    {
        var today = JalaliCalendar.Today;
        string D(int daysAgo) => JalaliCalendar.AddDays(today, -daysAgo);

        // Seed actors — a realistic mix of doctors, receptionists and the
        // manager, so the ledger's «کاربر» column/filter is demonstrably
        // populated (R1/R5).
        var ari = new ActorRef("u-aria", "دکتر آریا نیک", "مدیر کلینیک", "آ");
        var mina = new ActorRef("u-mina", "دکتر مینا همتی", "دکتر دامپزشک", "م");
        var soheil = new ActorRef("u-soheil", "دکتر سهیل کاویانی", "دکتر دامپزشک", "س");
        var sara = new ActorRef("u-sara", "منشی سارا", "پذیرش", "س");
        var negar = new ActorRef("u-negar", "منشی نگار", "پذیرش", "ن");
        var stock = ActorRef.System;

        var doctors = new[] { "دکتر مینا همتی", "دکتر آریا نیک", "دکتر سهیل کاویانی" };
        var owners = new[] { "حسین مرادی", "امیر رستگار", "الهام صادقی", "لیلا کریمی", "رضا نوروزی" };
        var pets = new[] { "کوکو", "سیمبا", "لو", "نانا", "آبی", "توتی", "بلا", "میلو", "رکسی", "گوچی" };
        var drugs = new[] { "آموکسی‌سیلین", "مترونیدازول", "پردنیزولون", "ویتامین B12", "آنتی‌فلی" };
        var banks = new[] { "ملت", "ملی", "سامان", "تجارت" };

        var events = new List<HistoryEvent>();

        void Add(int daysAgo, string time, HistoryEventKind kind, string title, string detail, string entity, long amount, int sign, HistoryEventStatus status, ActorRef actor)
        {
            var date = D(daysAgo);
            events.Add(new HistoryEvent($"{kind}-{date}-{time}-{events.Count}", kind, date, time, title, detail, entity, amount, sign, status, actor));
        }

        // Day 0 — today
        Add(0, "18:00", HistoryEventKind.Sale, "بستن صندوق — خدمات درمانی", "۹ مراجعه · کارتخوان", "کلینیک", 6_450_000, 1, HistoryEventStatus.Paid, sara);
        Add(0, "14:30", HistoryEventKind.Visit, "ویزیت عمومی — کوکو", owners[0], owners[0], 480_000, 1, HistoryEventStatus.Paid, mina);
        Add(0, "12:00", HistoryEventKind.Vaccine, "هاری — سیمبا", owners[3], owners[3], 260_000, 1, HistoryEventStatus.Done, soheil);
        Add(0, "10:00", HistoryEventKind.Expense, "خرید دارو", "", "تأمین‌کننده", 1_800_000, -1, HistoryEventStatus.Paid, ari);
        Add(0, "09:30", HistoryEventKind.Appointment, "ویزیت عمومی — نانا", owners[1], owners[1], 0, 0, HistoryEventStatus.Confirmed, negar);

        // Day 1 — yesterday
        Add(1, "17:30", HistoryEventKind.Sale, "فروش دارو — آموکسی‌سیلین", "۳ بسته · لیلا کریمی", owners[3], 540_000, 1, HistoryEventStatus.Paid, sara);
        Add(1, "16:00", HistoryEventKind.Cheque, "دریافت چک — ملت", owners[2], banks[0], 8_500_000, 1, HistoryEventStatus.Pending, ari);
        Add(1, "11:00", HistoryEventKind.Purchase, "خرید — ویتامین B12", "۶۰ عدد", "تأمین‌کننده", 4_200_000, -1, HistoryEventStatus.Paid, ari);
        Add(1, "10:00", HistoryEventKind.Boarding, "ورود پانسیون — آبی", "۴ شب · الهام صادقی", "کابین ۳", 1_200_000, 1, HistoryEventStatus.CheckIn, negar);
        Add(1, "09:00", HistoryEventKind.Patient, "ثبت بیمار جدید — توتی", "گربه", owners[4], 0, 0, HistoryEventStatus.New, negar);

        // Day 2
        Add(2, "18:00", HistoryEventKind.Sale, "بستن صندوق — پت‌شاپ", "۱۴ مراجعه · نقدی", "کلینیک", 9_800_000, 1, HistoryEventStatus.Paid, soheil);
        Add(2, "13:00", HistoryEventKind.Visit, "جراحی — بلا", owners[1], owners[1], 3_250_000, 1, HistoryEventStatus.Partial, ari);
        Add(2, "11:30", HistoryEventKind.Inventory, "هشدار موجودی — مترونیدازول", "موجودی: ۳ عدد", "داروخانه", 120_000, 0, HistoryEventStatus.LowStock, stock);

        // Day 3
        Add(3, "18:00", HistoryEventKind.Sale, "بستن صندوق — خدمات درمانی", "۱۱ مراجعه · انتقال", "کلینیک", 7_100_000, 1, HistoryEventStatus.Paid, mina);
        Add(3, "15:00", HistoryEventKind.Appointment, "دندانپزشکی — میلو", owners[3], owners[3], 0, 0, HistoryEventStatus.Done, negar);
        Add(3, "10:00", HistoryEventKind.Expense, "اجاره", "", "مالک", 12_000_000, -1, HistoryEventStatus.Paid, ari);

        // Day 4
        Add(4, "18:00", HistoryEventKind.Sale, "بستن صندوق — آزمایشگاه", "۸ مراجعه · کارتخوان", "کلینیک", 5_600_000, 1, HistoryEventStatus.Paid, sara);
        Add(4, "12:30", HistoryEventKind.Vaccine, "سه‌گانه گربه — رکسی", owners[2], owners[2], 320_000, 1, HistoryEventStatus.Done, mina);
        Add(4, "10:00", HistoryEventKind.Cheque, "پرداخت چک — سامان", "پخش دارویی سپهر", banks[2], 15_600_000, -1, HistoryEventStatus.Cleared, ari);

        // Day 5
        Add(5, "18:00", HistoryEventKind.Sale, "فروش دارو — پردنیزولون", "۲ بسته · رضا نوروزی", owners[4], 360_000, 1, HistoryEventStatus.Paid, sara);
        Add(5, "14:00", HistoryEventKind.Appointment, "آرایش و پیرایش — گوچی", owners[0], owners[0], 0, 0, HistoryEventStatus.Cancelled, negar);
        Add(5, "09:00", HistoryEventKind.Boarding, "ترخیص پانسیون — لو", "خروج: امروز", "کابین ۱", 800_000, 1, HistoryEventStatus.Discharge, mina);

        // Day 6
        Add(6, "18:00", HistoryEventKind.Sale, "بستن صندوق — داروخانه", "۱۰ مراجعه · نقدی", "کلینیک", 6_800_000, 1, HistoryEventStatus.Paid, sara);
        Add(6, "13:30", HistoryEventKind.Visit, "آزمایشگاه — سیمبا", owners[4], owners[4], 720_000, 1, HistoryEventStatus.Unpaid, ari);
        Add(6, "11:00", HistoryEventKind.Purchase, "خرید — آنتی‌فلی", "۲۰ عدد", "تأمین‌کننده", 2_300_000, -1, HistoryEventStatus.Unpaid, ari);

        // Day 8
        Add(8, "18:00", HistoryEventKind.Sale, "بستن صندوق — خدمات درمانی", "۱۳ مراجعه · کارتخوان", "کلینیک", 8_250_000, 1, HistoryEventStatus.Paid, soheil);
        Add(8, "15:00", HistoryEventKind.Cheque, "دریافت چک — تجارت", owners[1], banks[3], 4_500_000, 1, HistoryEventStatus.Bounced, ari);
        Add(8, "10:00", HistoryEventKind.Expense, "تعمیر", "", "خدمات", 950_000, -1, HistoryEventStatus.Paid, ari);

        // Day 11
        Add(11, "18:00", HistoryEventKind.Sale, "بستن صندوق — پت‌شاپ", "۷ مراجعه · انتقال", "کلینیک", 4_900_000, 1, HistoryEventStatus.Paid, ari);
        Add(11, "12:00", HistoryEventKind.Vaccine, "پاروو — نانا", owners[3], owners[3], 280_000, 1, HistoryEventStatus.Done, mina);
        Add(11, "09:30", HistoryEventKind.Patient, "ثبت بیمار جدید — بلا", "سگ", owners[2], 0, 0, HistoryEventStatus.New, negar);

        // Day 15
        Add(15, "18:00", HistoryEventKind.Sale, "بستن صندوق — خدمات درمانی", "۱۶ مراجعه · کارتخوان", "کلینیک", 10_400_000, 1, HistoryEventStatus.Paid, mina);
        Add(15, "11:00", HistoryEventKind.Appointment, "جراحی — آبی", owners[4], owners[4], 0, 0, HistoryEventStatus.Done, negar);

        // Day 19
        Add(19, "18:00", HistoryEventKind.Sale, "بستن صندوق — آزمایشگاه", "۹ مراجعه · نقدی", "کلینیک", 6_100_000, 1, HistoryEventStatus.Paid, soheil);
        Add(19, "10:00", HistoryEventKind.Expense, "حقوق", "", "پرسنل", 18_000_000, -1, HistoryEventStatus.Paid, ari);

        // Day 23
        Add(23, "18:00", HistoryEventKind.Sale, "بستن صندوق — داروخانه", "۱۲ مراجعه · کارتخوان", "کلینیک", 7_750_000, 1, HistoryEventStatus.Paid, ari);
        Add(23, "13:00", HistoryEventKind.Boarding, "ورود پانسیون — کوکو", "۶ شب · حسین مرادی", "کابین ۵", 1_800_000, 1, HistoryEventStatus.CheckIn, negar);

        // Day 27
        Add(27, "18:00", HistoryEventKind.Sale, "بستن صندوق — پت‌شاپ", "۶ مراجعه · نقدی", "کلینیک", 4_300_000, 1, HistoryEventStatus.Paid, mina);
        Add(27, "11:00", HistoryEventKind.Inventory, "هشدار موجودی — ویتامین B12", "موجودی: ۴ عدد", "داروخانه", 210_000, 0, HistoryEventStatus.LowStock, stock);

        // Day 30
        Add(30, "18:00", HistoryEventKind.Sale, "بستن صندوق — خدمات درمانی", "۱۰ مراجعه · انتقال", "کلینیک", 6_900_000, 1, HistoryEventStatus.Paid, soheil);
        Add(30, "10:00", HistoryEventKind.Expense, "بازاریابی", "", "تبلیغات", 3_400_000, -1, HistoryEventStatus.Paid, ari);

        // Newest-first (the reference's descending sort by its synthetic sort key).
        return events
            .OrderByDescending(e => e.Date, StringComparer.Ordinal)
            .ThenByDescending(e => e.Time, StringComparer.Ordinal)
            .ThenBy(e => e.Title, StringComparer.Ordinal)
            .ToList();
    }
}
