using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Animora.Contracts.History;
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
/// </summary>
public sealed class MockHistoryQueryService : IHistoryQueryService
{
    private readonly IReadOnlyList<HistoryEvent> _events;

    public MockHistoryQueryService()
    {
        _events = BuildEvents();
    }

    public Task<IReadOnlyList<HistoryEvent>> GetEventsAsync(HistoryFilter filter)
    {
        IReadOnlyList<HistoryEvent> result = Filter(_events, filter);
        return Task.FromResult(result);
    }

    public Task<string> ExportCsvAsync(HistoryFilter filter)
    {
        var events = Filter(_events, filter);
        var builder = new StringBuilder();

        // The reference's header row (Animora-UI/assets/history.js exportCSV).
        builder.AppendLine(CsvRow("تاریخ", "زمان", "دسته", "رویداد", "توضیح", "موجودیت", "مبلغ", "وضعیت"));

        foreach (var ev in events)
        {
            var amount = ev.AmountSign < 0 ? "-" + ev.Amount : (ev.AmountSign > 0 ? ev.Amount.ToString(CultureInfo.InvariantCulture) : "0");
            builder.AppendLine(CsvRow(
                ev.Date,
                ev.Time,
                HistoryPresentation.KindLabel(ev.Kind),
                ev.Title,
                ev.Detail,
                ev.Entity,
                amount,
                HistoryPresentation.StatusLabel(ev.Status)));
        }

        return Task.FromResult(builder.ToString());

        static string CsvRow(params string[] cells) =>
            string.Join(',', cells.Select(c => "\"" + c.Replace("\"", "\"\"") + "\""));
    }

    // ── Filtering (mirrors the reference's period/cats/search predicates) ───

    private static List<HistoryEvent> Filter(IReadOnlyList<HistoryEvent> source, HistoryFilter filter)
    {
        var start = PeriodStart(filter.Period);
        var kinds = filter.Kinds;
        var search = filter.Search?.Trim() ?? string.Empty;

        var result = source.Where(ev =>
        {
            if (start is not null && string.CompareOrdinal(ev.Date, start) < 0)
            {
                return false;
            }

            if (kinds.Count > 0 && !kinds.Contains(ev.Kind))
            {
                return false;
            }

            if (search.Length > 0
                && !ev.Title.Contains(search, StringComparison.Ordinal)
                && !ev.Detail.Contains(search, StringComparison.Ordinal)
                && !ev.Entity.Contains(search, StringComparison.Ordinal))
            {
                return false;
            }

            return true;
        }).ToList();

        return result;
    }

    /// <summary>The Jalali start date of a period id, or null for «همه» (the reference's <c>periodStart</c>).</summary>
    private static string? PeriodStart(string period)
    {
        var today = JalaliCalendar.Today;
        return period switch
        {
            "today" => today,
            "7" => JalaliCalendar.AddDays(today, -6),
            "30" => JalaliCalendar.AddDays(today, -29),
            _ => null,
        };
    }

    // ── Synthetic seed data ─────────────────────────────────────────────────

    private static IReadOnlyList<HistoryEvent> BuildEvents()
    {
        var today = JalaliCalendar.Today;
        string D(int daysAgo) => JalaliCalendar.AddDays(today, -daysAgo);

        var doctors = new[] { "دکتر مینا همتی", "دکتر آریا نیک", "دکتر سهیل کاویانی" };
        var owners = new[] { "حسین مرادی", "امیر رستگار", "الهام صادقی", "لیلا کریمی", "رضا نوروزی" };
        var pets = new[] { "کوکو", "سیمبا", "لو", "نانا", "آبی", "توتی", "بلا", "میلو", "رکسی", "گوچی" };
        var drugs = new[] { "آموکسی‌سیلین", "مترونیدازول", "پردنیزولون", "ویتامین B12", "آنتی‌فلی" };
        var banks = new[] { "ملت", "ملی", "سامان", "تجارت" };

        var events = new List<HistoryEvent>();

        void Add(int daysAgo, string time, HistoryEventKind kind, string title, string detail, string entity, long amount, int sign, HistoryEventStatus status)
        {
            var date = D(daysAgo);
            events.Add(new HistoryEvent($"{kind}-{date}-{time}-{events.Count}", kind, date, time, title, detail, entity, amount, sign, status));
        }

        // Day 0 — today
        Add(0, "18:00", HistoryEventKind.Sale, "بستن صندوق — خدمات درمانی", "۹ مراجعه · کارتخوان", doctors[0], 6_450_000, 1, HistoryEventStatus.Paid);
        Add(0, "14:30", HistoryEventKind.Visit, "ویزیت عمومی — کوکو", owners[0], doctors[1], 480_000, 1, HistoryEventStatus.Paid);
        Add(0, "12:00", HistoryEventKind.Vaccine, "هاری — سیمبا", owners[3], doctors[2], 260_000, 1, HistoryEventStatus.Done);
        Add(0, "10:00", HistoryEventKind.Expense, "خرید دارو", "", "", 1_800_000, -1, HistoryEventStatus.Paid);
        Add(0, "09:30", HistoryEventKind.Appointment, "ویزیت عمومی — نانا", owners[1], doctors[0], 0, 0, HistoryEventStatus.Confirmed);

        // Day 1 — yesterday
        Add(1, "17:30", HistoryEventKind.Sale, "فروش دارو — آموکسی‌سیلین", "۳ بسته · لیلا کریمی", "", 540_000, 1, HistoryEventStatus.Paid);
        Add(1, "16:00", HistoryEventKind.Cheque, "دریافت چک — ملت", owners[2], banks[0], 8_500_000, 1, HistoryEventStatus.Pending);
        Add(1, "11:00", HistoryEventKind.Purchase, "خرید — ویتامین B12", "۶۰ عدد", "تأمین‌کننده", 4_200_000, -1, HistoryEventStatus.Paid);
        Add(1, "10:00", HistoryEventKind.Boarding, "ورود پانسیون — آبی", "۴ شب · الهام صادقی", "کابین ۳", 1_200_000, 1, HistoryEventStatus.CheckIn);
        Add(1, "09:00", HistoryEventKind.Patient, "ثبت بیمار جدید — توتی", "گربه", owners[4], 0, 0, HistoryEventStatus.New);

        // Day 2
        Add(2, "18:00", HistoryEventKind.Sale, "بستن صندوق — پت‌شاپ", "۱۴ مراجعه · نقدی", doctors[2], 9_800_000, 1, HistoryEventStatus.Paid);
        Add(2, "13:00", HistoryEventKind.Visit, "جراحی — بلا", owners[1], doctors[0], 3_250_000, 1, HistoryEventStatus.Partial);
        Add(2, "11:30", HistoryEventKind.Inventory, "هشدار موجودی — مترونیدازول", "موجودی: ۳ عدد", "داروخانه", 120_000, 0, HistoryEventStatus.LowStock);

        // Day 3
        Add(3, "18:00", HistoryEventKind.Sale, "بستن صندوق — خدمات درمانی", "۱۱ مراجعه · انتقال", doctors[1], 7_100_000, 1, HistoryEventStatus.Paid);
        Add(3, "15:00", HistoryEventKind.Appointment, "دندانپزشکی — میلو", owners[3], doctors[2], 0, 0, HistoryEventStatus.Done);
        Add(3, "10:00", HistoryEventKind.Expense, "اجاره", "", "", 12_000_000, -1, HistoryEventStatus.Paid);

        // Day 4
        Add(4, "18:00", HistoryEventKind.Sale, "بستن صندوق — آزمایشگاه", "۸ مراجعه · کارتخوان", doctors[0], 5_600_000, 1, HistoryEventStatus.Paid);
        Add(4, "12:30", HistoryEventKind.Vaccine, "سه‌گانه گربه — رکسی", owners[2], doctors[1], 320_000, 1, HistoryEventStatus.Done);
        Add(4, "10:00", HistoryEventKind.Cheque, "پرداخت چک — سامان", "پخش دارویی سپهر", banks[2], 15_600_000, -1, HistoryEventStatus.Cleared);

        // Day 5
        Add(5, "18:00", HistoryEventKind.Sale, "فروش دارو — پردنیزولون", "۲ بسته · رضا نوروزی", "", 360_000, 1, HistoryEventStatus.Paid);
        Add(5, "14:00", HistoryEventKind.Appointment, "آرایش و پیرایش — گوچی", owners[0], doctors[2], 0, 0, HistoryEventStatus.Cancelled);
        Add(5, "09:00", HistoryEventKind.Boarding, "ترخیص پانسیون — لو", "خروج: امروز", "کابین ۱", 800_000, 1, HistoryEventStatus.Discharge);

        // Day 6
        Add(6, "18:00", HistoryEventKind.Sale, "بستن صندوق — داروخانه", "۱۰ مراجعه · نقدی", doctors[1], 6_800_000, 1, HistoryEventStatus.Paid);
        Add(6, "13:30", HistoryEventKind.Visit, "آزمایشگاه — سیمبا", owners[4], doctors[0], 720_000, 1, HistoryEventStatus.Unpaid);
        Add(6, "11:00", HistoryEventKind.Purchase, "خرید — آنتی‌فلی", "۲۰ عدد", "تأمین‌کننده", 2_300_000, -1, HistoryEventStatus.Unpaid);

        // Day 8
        Add(8, "18:00", HistoryEventKind.Sale, "بستن صندوق — خدمات درمانی", "۱۳ مراجعه · کارتخوان", doctors[2], 8_250_000, 1, HistoryEventStatus.Paid);
        Add(8, "15:00", HistoryEventKind.Cheque, "دریافت چک — تجارت", owners[1], banks[3], 4_500_000, 1, HistoryEventStatus.Bounced);
        Add(8, "10:00", HistoryEventKind.Expense, "تعمیر", "", "", 950_000, -1, HistoryEventStatus.Paid);

        // Day 11
        Add(11, "18:00", HistoryEventKind.Sale, "بستن صندوق — پت‌شاپ", "۷ مراجعه · انتقال", doctors[0], 4_900_000, 1, HistoryEventStatus.Paid);
        Add(11, "12:00", HistoryEventKind.Vaccine, "پاروو — نانا", owners[3], doctors[1], 280_000, 1, HistoryEventStatus.Done);
        Add(11, "09:30", HistoryEventKind.Patient, "ثبت بیمار جدید — بلا", "سگ", owners[2], 0, 0, HistoryEventStatus.New);

        // Day 15
        Add(15, "18:00", HistoryEventKind.Sale, "بستن صندوق — خدمات درمانی", "۱۶ مراجعه · کارتخوان", doctors[1], 10_400_000, 1, HistoryEventStatus.Paid);
        Add(15, "11:00", HistoryEventKind.Appointment, "جراحی — آبی", owners[4], doctors[0], 0, 0, HistoryEventStatus.Done);

        // Day 19
        Add(19, "18:00", HistoryEventKind.Sale, "بستن صندوق — آزمایشگاه", "۹ مراجعه · نقدی", doctors[2], 6_100_000, 1, HistoryEventStatus.Paid);
        Add(19, "10:00", HistoryEventKind.Expense, "حقوق", "", "", 18_000_000, -1, HistoryEventStatus.Paid);

        // Day 23
        Add(23, "18:00", HistoryEventKind.Sale, "بستن صندوق — داروخانه", "۱۲ مراجعه · کارتخوان", doctors[0], 7_750_000, 1, HistoryEventStatus.Paid);
        Add(23, "13:00", HistoryEventKind.Boarding, "ورود پانسیون — کوکو", "۶ شب · حسین مرادی", "کابین ۵", 1_800_000, 1, HistoryEventStatus.CheckIn);

        // Day 27
        Add(27, "18:00", HistoryEventKind.Sale, "بستن صندوق — پت‌شاپ", "۶ مراجعه · نقدی", doctors[1], 4_300_000, 1, HistoryEventStatus.Paid);
        Add(27, "11:00", HistoryEventKind.Inventory, "هشدار موجودی — ویتامین B12", "موجودی: ۴ عدد", "داروخانه", 210_000, 0, HistoryEventStatus.LowStock);

        // Day 30
        Add(30, "18:00", HistoryEventKind.Sale, "بستن صندوق — خدمات درمانی", "۱۰ مراجعه · انتقال", doctors[2], 6_900_000, 1, HistoryEventStatus.Paid);
        Add(30, "10:00", HistoryEventKind.Expense, "بازاریابی", "", "", 3_400_000, -1, HistoryEventStatus.Paid);

        // Newest-first (the reference's descending sort by its synthetic sort key).
        return events
            .OrderByDescending(e => e.Date, StringComparer.Ordinal)
            .ThenByDescending(e => e.Time, StringComparer.Ordinal)
            .ThenBy(e => e.Title, StringComparer.Ordinal)
            .ToList();
    }
}
