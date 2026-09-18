using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Animora.Contracts.Finance;
using Animora.Desktop.Localization;

namespace Animora.Desktop.Mocks;

/// <summary>
/// In-memory Finance data source (the single backend swap point, R-18). It
/// re-derives the reference's deterministic dataset
/// (Animora-UI/assets/finance-data.js) — a 90-day seeded daily ledger, the
/// revenue/expense catalogs, the top-product list, species split, insights,
/// payment split and the visit heatmap — and reproduces the reference's
/// <c>aggregate(range)</c>/<c>bucketSeries</c> arithmetic with .NET idioms.
/// No JS is copied: only the numbers/shape the screen visually needs.
/// </summary>
public sealed class MockFinanceQueryService : IFinanceQueryService
{
    // ── Catalogs (mirroring REVENUE_CHANNELS / EXPENSE_CATEGORIES / PAYMENT_METHODS) ──

    private sealed record ChannelDef(string Id, string Label, string Icon, string Color);

    private sealed record CategoryDef(string Id, string Label, string Icon, string Color, string Tone);

    private static readonly ChannelDef[] Channels =
    [
        new("clinic", "خدمات درمانی", "IconClipboard", "#0d9488"),
        new("pharmacy", "داروخانه", "IconFlask", "#5c8ad6"),
        new("petshop", "پت‌شاپ", "IconCart", "#b8a2e6"),
        new("lab", "آزمایشگاه", "IconSpark", "#eab749"),
    ];

    private static readonly CategoryDef[] Categories =
    [
        new("salary", "حقوق و دستمزد", "IconUsers", "#0d9488", "teal"),
        new("rent", "اجاره و قبوض", "IconHome", "#e0a95b", "amber"),
        new("supply", "خرید دارو و کالا", "IconBox", "#5c8ad6", "blue"),
        new("marketing", "بازاریابی", "IconMegaphone", "#b8a2e6", "lilac"),
        new("repair", "تعمیر و نگهداری", "IconTools", "#e08a5c", "peach"),
        new("tax", "مالیات و بیمه", "IconShield", "#7a8b89", "mute"),
        new("other", "متفرقه", "IconMore", "#b54708", "warn"),
    ];

    private static readonly (string Id, string Label, string Color)[] PaymentMethods =
    [
        ("pos", "کارتخوان", "#0d9488"),
        ("cash", "نقدی", "#8fb5f0"),
        ("transfer", "انتقال", "#e08a5c"),
        ("credit", "اعتباری", "#b8a2e6"),
    ];

    private static readonly int[] PaymentSplit = [49, 21, 17, 13];

    private static readonly FinanceRange[] Ranges =
    [
        new("7d", "۷ روز اخیر", 7, "day"),
        new("30d", "۳۰ روز اخیر", 30, "day"),
        new("90d", "۳ ماه اخیر", 90, "week"),
        new("ytd", "از ابتدای سال", 90, "week"),
    ];

    private static readonly FinanceProductSort[] ProductSorts =
    [
        new("revenue", "پردرآمدترین"),
        new("qty", "پرفروش‌ترین"),
        new("profit", "پرسودترین"),
    ];

    // TOP_PRODUCTS seed (profit/margin derived exactly like the reference).
    private static readonly FinanceProduct[] Products =
    [
        new("ویزیت عمومی", "خدمات درمانی", "service", "IconClipboard", "teal", 132, 47_520_000, 12_800_000),
        new("غذای خشک گربه رویال", "پت‌شاپ", "product", "IconCart", "blue", 88, 30_800_000, 22_600_000),
        new("واکسن سه‌گانه سگ", "واکسیناسیون", "service", "IconSyringe", "peach", 76, 26_600_000, 11_400_000),
        new("آزمایش CBC کامل", "آزمایشگاه", "service", "IconFlask", "lilac", 64, 25_600_000, 7_680_000),
        new("سونوگرافی شکمی", "خدمات درمانی", "service", "IconSpark", "teal", 41, 24_600_000, 6_150_000),
        new("قرص ضد انگل داخلی", "داروخانه", "product", "IconFlask", "blue", 154, 18_480_000, 9_240_000),
        new("جراحی عقیم‌سازی گربه", "خدمات درمانی", "service", "IconScissors", "teal", 18, 21_600_000, 5_400_000),
        new("شامپو ضد کنه", "پت‌شاپ", "product", "IconBath", "blue", 63, 9_450_000, 6_930_000),
    ];

    private static readonly (string Label, long Value, string Color)[] Species =
    [
        ("سگ", 168, "#0d9488"),
        ("گربه", 142, "#8fb5f0"),
        ("پرنده", 68, "#eab749"),
        ("خرگوش", 40, "#b8a2e6"),
        ("خزنده", 14, "#e08a5c"),
    ];

    private static readonly FinanceInsight[] Insights =
    [
        new("IconTrendUp", "ok", "حاشیه سود «سونوگرافی شکمی» با ۷۵٪ بالاترین سودآوری را دارد؛ ظرفیت رزرو آن را افزایش دهید."),
        new("IconAlert", "warn", "هزینه «خرید دارو و کالا» نسبت به بازه قبل ۱۲٪ رشد داشته — قیمت تأمین‌کنندگان را بازبینی کنید."),
        new("IconBulb", "info", "پنج‌شنبه‌ها پرترافیک‌ترین روز هفته است؛ افزودن یک شیفت می‌تواند درآمد را بیشتر کند."),
        new("IconPiggy", "lilac", "با روند فعلی، سود خالص این ماه حدود ۱۸٪ بیشتر از ماه گذشته پیش‌بینی می‌شود."),
    ];

    // HEATMAP — 7 days (شنبه..جمعه) × 12 hours (۹..۲۰), intensity 0..5.
    private static readonly int[][] Heatmap =
    [
        [1, 0, 1, 2, 3, 2, 4, 3, 4, 4, 3, 2],
        [0, 1, 2, 3, 4, 3, 4, 4, 4, 4, 3, 1],
        [1, 1, 2, 3, 4, 4, 3, 4, 3, 4, 3, 2],
        [1, 2, 3, 4, 4, 3, 4, 4, 4, 3, 2, 1],
        [1, 2, 4, 4, 3, 3, 4, 3, 4, 4, 3, 2],
        [2, 3, 4, 4, 4, 4, 4, 4, 3, 3, 2, 1],
        [0, 0, 1, 1, 2, 2, 3, 2, 2, 1, 1, 0],
    ];

    // Weekday weighting: Thursday busy, Friday quiet (0 = شنبه .. 6 = جمعه).
    private static readonly double[] DowWeight = [1.0, 1.05, 1.1, 1.0, 1.15, 1.35, 0.55];

    private readonly List<DailyRow> _daily;

    public MockFinanceQueryService()
    {
        _daily = BuildDaily();
    }

    public Task<IReadOnlyList<FinanceRange>> GetRangesAsync() =>
        Task.FromResult<IReadOnlyList<FinanceRange>>(Ranges);

    public Task<IReadOnlyList<FinanceProductSort>> GetProductSortsAsync() =>
        Task.FromResult<IReadOnlyList<FinanceProductSort>>(ProductSorts);

    public Task<FinanceOverview> GetOverviewAsync(string range)
    {
        var agg = Aggregate(range);
        return Task.FromResult(new FinanceOverview(
            agg.Range.Label,
            agg.Range.Days,
            agg.RevTotal,
            agg.ExpTotal,
            agg.Profit,
            agg.Margin,
            agg.Delta,
            agg.AvgDailyProfit,
            agg.Series,
            agg.ByChannel,
            agg.ByCategory,
            Products,
            BuildServices(),
            Insights,
            Species.Select(s => new FinanceSpecies(s.Label, s.Value, s.Color)).ToArray(),
            BuildPayments(),
            Heatmap));
    }

    public Task<FinanceIncome> GetIncomeAsync(string range)
    {
        var agg = Aggregate(range);
        var maxChannel = Math.Max(1, agg.ByChannel.Max(c => c.Value));
        var total = agg.RevTotal == 0 ? 1 : agg.RevTotal;
        var bars = agg.ByChannel
            .OrderByDescending(c => c.Value)
            .Select(c => new FinanceChannelBar(
                c.Id, c.Label, c.Icon, c.Color, c.Value, Math.Round((double)c.Value / total * 100)))
            .ToArray();
        _ = maxChannel;

        return Task.FromResult(new FinanceIncome(
            agg.Range.Label,
            agg.Range.Days,
            agg.RevTotal,
            agg.ExpTotal,
            agg.Profit,
            agg.Margin,
            agg.Delta,
            agg.AvgDailyProfit,
            agg.Series,
            agg.ByChannel,
            agg.ByCategory,
            bars,
            Products,
            BuildServices(),
            Insights,
            Species.Select(s => new FinanceSpecies(s.Label, s.Value, s.Color)).ToArray(),
            BuildPayments(),
            Heatmap));
    }

    // ── Aggregation engine ───────────────────────────────────────────────

    private Agg Aggregate(string rangeId)
    {
        var range = Ranges.FirstOrDefault(r => r.Id == rangeId) ?? Ranges[1];
        var n = range.Days;
        var slice = _daily.Skip(_daily.Count - n).ToArray();
        var prevSlice = _daily.Skip(_daily.Count - (2 * n)).Take(n).ToArray();

        long revTotal = slice.Sum(r => r.RevTotal);
        long expTotal = slice.Sum(r => r.ExpTotal);
        long profit = revTotal - expTotal;
        double margin = revTotal == 0 ? 0 : (double)profit / revTotal * 100;

        long prevRev = prevSlice.Sum(r => r.RevTotal);
        long prevExp = prevSlice.Sum(r => r.ExpTotal);
        long prevProfit = prevRev - prevExp;
        double prevMargin = prevRev == 0 ? 0 : (double)prevProfit / prevRev * 100;

        var byChannel = Channels
            .Select(c => new FinanceChannel(c.Id, c.Label, c.Icon, c.Color, slice.Sum(r => r.Revenue[c.Id])))
            .ToArray();

        var byCategory = Categories
            .Select(c =>
            {
                long value = slice.Sum(r => r.Expense[c.Id]);
                long prev = prevSlice.Sum(r => r.Expense[c.Id]);
                return new FinanceCategory(c.Id, c.Label, c.Icon, c.Color, c.Tone, value, PctDelta(value, prev));
            })
            .OrderByDescending(c => c.Value)
            .ToArray();

        return new Agg(
            range,
            revTotal,
            expTotal,
            profit,
            margin,
            new FinanceDeltas(
                PctDelta(revTotal, prevRev),
                PctDelta(expTotal, prevExp),
                PctDelta(profit, prevProfit),
                margin - prevMargin),
            (double)profit / n,
            BucketSeries(slice, range.Bucket),
            byChannel,
            byCategory);
    }

    private static double PctDelta(double cur, double prev) => prev == 0 ? 0 : (cur - prev) / prev * 100;

    private static IReadOnlyList<FinanceSeriesPoint> BucketSeries(DailyRow[] slice, string bucket)
    {
        if (bucket == "day")
        {
            return slice.Select(r => new FinanceSeriesPoint(
                LabelForDay(r), r.Date, r.RevTotal, r.ExpTotal, r.Profit)).ToArray();
        }

        var outList = new List<FinanceSeriesPoint>();
        for (var i = 0; i < slice.Length; i += 7)
        {
            var grp = slice.Skip(i).Take(7).ToArray();
            if (grp.Length == 0)
            {
                continue;
            }

            outList.Add(new FinanceSeriesPoint(
                "هفته " + Fa((i / 7) + 1),
                grp[0].Date + " تا " + grp[^1].Date,
                grp.Sum(r => r.RevTotal),
                grp.Sum(r => r.ExpTotal),
                grp.Sum(r => r.Profit)));
        }

        return outList;
    }

    private static string LabelForDay(DailyRow r)
    {
        var p = JalaliCalendar.Parse(r.Date);
        return Fa(p.Day) + " " + PersianMonthNames.Get(p.Month);
    }

    private static IReadOnlyList<FinanceServiceSplit> BuildServices()
    {
        long svcRev = 0, svcProfit = 0;
        var svcQty = 0;
        long prodRev = 0, prodProfit = 0;
        var prodQty = 0;
        foreach (var p in Products)
        {
            if (p.Kind == "service")
            {
                svcRev += p.Revenue;
                svcProfit += p.Profit;
                svcQty += p.Qty;
            }
            else
            {
                prodRev += p.Revenue;
                prodProfit += p.Profit;
                prodQty += p.Qty;
            }
        }

        return
        [
            new FinanceServiceSplit("service", "خدمات و ویزیت", "IconClipboard", "#0d9488", svcQty, svcRev, svcProfit),
            new FinanceServiceSplit("product", "فروش کالا و دارو", "IconCart", "#5c8ad6", prodQty, prodRev, prodProfit),
        ];
    }

    private static IReadOnlyList<FinancePaymentSplit> BuildPayments() =>
        PaymentSplit
            .Select((value, i) => new FinancePaymentSplit(
                PaymentMethods[i].Id, PaymentMethods[i].Label, PaymentMethods[i].Color, value))
            .ToArray();

    // ── Daily ledger (deterministic, mirrors the reference's seeded generator) ──

    private static List<DailyRow> BuildDaily()
    {
        var rng = MakeRng(20250816u);
        var today = JalaliCalendar.Today;
        var todayDay = JalaliCalendar.ToDay(today);
        var rows = new List<DailyRow>(90);

        for (var i = 89; i >= 0; i--)
        {
            var dayAbs = todayDay - i;
            var jdate = JalaliCalendar.FromDay(dayAbs);
            var dow = JalaliCalendar.Weekday(jdate);
            var w = DowWeight[dow];
            var trend = 1 + (89 - i) / 89.0 * 0.28;
            var noise = 0.82 + (rng() * 0.36);

            var book = 11.8e6 * w * trend * noise;

            var revenue = new Dictionary<string, long>(StringComparer.Ordinal)
            {
                ["clinic"] = (long)Math.Round(book),
                ["pharmacy"] = (long)Math.Round(book * (0.5 + (rng() * 0.2))),
                ["petshop"] = (long)Math.Round(book * (0.28 + (rng() * 0.16))),
                ["lab"] = (long)Math.Round(book * (0.14 + (rng() * 0.1))),
            };

            var en = 0.9 + (rng() * 0.2);
            var expense = new Dictionary<string, long>(StringComparer.Ordinal)
            {
                ["salary"] = (long)Math.Round(4.9e6 * en),
                ["rent"] = (long)Math.Round(2.7e6 * en),
                ["supply"] = (long)Math.Round(book * (0.34 + (rng() * 0.12))),
                ["marketing"] = (long)Math.Round(1.4e6 * (0.6 + (rng() * 0.9))),
                ["repair"] = (long)Math.Round(0.9e6 * (0.2 + (rng() * 1.4))),
                ["tax"] = (long)Math.Round(1.6e6 * en),
                ["other"] = (long)Math.Round(0.7e6 * (0.3 + (rng() * 1.2))),
            };

            rows.Add(new DailyRow(jdate, revenue, expense));
        }

        return rows;
    }

    /// <summary>
    /// A faithful C# port of the reference's <c>mulberry32</c> PRNG (32-bit
    /// unsigned arithmetic ≡ the JS <c>|0</c>/<c>Math.imul</c> semantics), so
    /// the generated series is stable across runs — the desktop analogue of the
    /// prototype's screenshot-stable seeded data.
    /// </summary>
    private static Func<double> MakeRng(uint seed)
    {
        var a = seed;
        return () =>
        {
            a += 0x6D2B79F5u;
            var t = a;
            t = (t ^ (t >> 15)) * (1u | t);
            t = (t + ((t ^ (t >> 7)) * (61u | t))) ^ t;
            return ((t ^ (t >> 14)) & 0xFFFFFFFFu) / 4294967296.0;
        };
    }

    private static string Fa(long value) =>
        PersianNumberFormatter.ToPersianDigits(value.ToString(CultureInfo.InvariantCulture));

    private sealed record DailyRow(
        string Date, Dictionary<string, long> Revenue, Dictionary<string, long> Expense)
    {
        public long RevTotal { get; } = Revenue.Values.Sum();

        public long ExpTotal { get; } = Expense.Values.Sum();

        public long Profit => RevTotal - ExpTotal;
    }

    private sealed record Agg(
        FinanceRange Range,
        long RevTotal,
        long ExpTotal,
        long Profit,
        double Margin,
        FinanceDeltas Delta,
        double AvgDailyProfit,
        IReadOnlyList<FinanceSeriesPoint> Series,
        IReadOnlyList<FinanceChannel> ByChannel,
        IReadOnlyList<FinanceCategory> ByCategory);
}
