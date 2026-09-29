using System.Globalization;
using Animora.Application.UseCases.Finance.ReadModels;
using Animora.Contracts.Finance;
using Animora.Domain.Finance;
using Animora.Domain.Sales;

namespace Animora.Client.Services.Finance;

/// <summary>Presentation-only catalog; amounts and products always come from projected facts.</summary>
internal static class FinanceMapping
{
    private sealed record Meta(string Id, string Label, string Icon, string Color, string Tone = "");
    private static readonly Meta[] Channels =
    [
        new("clinic", "خدمات درمانی", "IconClipboard", "#0d9488"),
        new("pharmacy", "داروخانه", "IconFlask", "#5c8ad6"),
        new("petshop", "پت‌شاپ", "IconCart", "#b8a2e6"),
        new("lab", "آزمایشگاه", "IconSpark", "#eab749"),
    ];
    private static readonly Meta[] Categories =
    [
        new("salary", "حقوق و دستمزد", "IconUsers", "#0d9488", "teal"),
        new("rent", "اجاره و قبوض", "IconHome", "#e0a95b", "amber"),
        new("supply", "خرید دارو و کالا", "IconBox", "#5c8ad6", "blue"),
        new("marketing", "بازاریابی", "IconMegaphone", "#b8a2e6", "lilac"),
        new("repair", "تعمیر و نگهداری", "IconTools", "#e08a5c", "peach"),
        new("tax", "مالیات و بیمه", "IconShield", "#7a8b89", "mute"),
        new("other", "متفرقه", "IconMore", "#b54708", "warn"),
    ];
    private static readonly (PaymentMethod Method, string Id, string Label, string Color)[] Methods =
    [
        (PaymentMethod.Card, "pos", "کارتخوان", "#0d9488"),
        (PaymentMethod.Cash, "cash", "نقدی", "#8fb5f0"),
        (PaymentMethod.Transfer, "transfer", "انتقال", "#e08a5c"),
        (PaymentMethod.Credit, "credit", "اعتباری", "#b8a2e6"),
        (PaymentMethod.Cheque, "cheque", "چک", "#eab749"),
        (PaymentMethod.Insurance, "insurance", "بیمه", "#b8a2e6"),
        (PaymentMethod.Wallet, "wallet", "کیف پول", "#0d9488"),
    ];
    private static readonly IReadOnlyList<FinanceInsight> Insights =
    [
        new("IconTrendUp", "ok", "روند درآمد و سود را در نمودارهای این بازه دنبال کنید."),
        new("IconAlert", "warn", "تغییرات هزینه را در مقایسه با بازه قبل بررسی کنید."),
        new("IconBulb", "info", "جزئیات فروش کالا و خدمات در جدول پایین آمده است."),
        new("IconPiggy", "lilac", "گزارش مالی بر اساس داده‌های ثبت‌شده محاسبه می‌شود."),
    ];
    // The visit/species widgets are visual-only; these values are not finance facts.
    private static readonly IReadOnlyList<FinanceSpecies> Species =
    [
        new("سگ", 168, "#0d9488"), new("گربه", 142, "#8fb5f0"),
        new("پرنده", 68, "#eab749"), new("خرگوش", 40, "#b8a2e6"),
        new("خزنده", 14, "#e08a5c"),
    ];
    private static readonly IReadOnlyList<IReadOnlyList<int>> Heatmap = new int[][]
    {
        [1,0,1,2,3,2,4,3,4,4,3,2], [0,1,2,3,4,3,4,4,4,4,3,1],
        [1,1,2,3,4,4,3,4,3,4,3,2], [1,2,3,4,4,3,4,4,4,3,2,1],
        [1,2,4,4,3,3,4,3,4,4,3,2], [2,3,4,4,4,4,4,4,3,3,2,1],
        [0,0,1,1,2,2,3,2,2,1,1,0],
    }.Select(row => (IReadOnlyList<int>)row).ToArray();
    internal static readonly IReadOnlyList<FinanceProductSort> ProductSorts =
    [new("revenue", "پردرآمدترین"), new("qty", "پرفروش‌ترین"), new("profit", "پرسودترین")];

    internal static readonly FinanceOverview EmptyOverview = new("", 0, 0, 0, 0, 0,
        new(0, 0, 0, 0), 0, [], [], [], [], [], [], [], [], []);
    internal static readonly FinanceIncome EmptyIncome = new("", 0, 0, 0, 0, 0,
        new(0, 0, 0, 0), 0, [], [], [], [], [], [], [], [], [], []);
    internal static readonly FinanceExpense EmptyExpense = new("", 0, 0, 0, new(0, 0, 0, 0), [], [], []);
    internal static readonly FinanceProfit EmptyProfit = new("", 0, 0, 0, 0, 0,
        new(0, 0, 0, 0), 0, [], [], [], []);

    private static Meta Category(string code) => code switch
    {
        "SALARY" => Categories[0], "RENT" or "UTILITIES" => Categories[1],
        "SUPPLIES" => Categories[2], "MARKETING" => Categories[3],
        "MAINTENANCE" => Categories[4], "TAX" => Categories[5], _ => Categories[6],
    };
    internal static string CategoryCode(string id) => id switch
    {
        "salary" => "SALARY", "rent" => "RENT", "supply" => "SUPPLIES",
        "marketing" => "MARKETING", "repair" => "MAINTENANCE", "tax" => "TAX",
        "other" => "OTHER", _ => throw new ArgumentException("Unknown expense category", nameof(id)),
    };
    internal static FinanceCategoryOption CategoryOption(string code)
    {
        var m = Category(code);
        return new(m.Id, m.Label, m.Color);
    }
    internal static PaymentMethod ParseMethod(string id) => Methods.FirstOrDefault(m => m.Id == id) is { Id: not null } match
        ? match.Method : throw new ArgumentException("Unknown payment method", nameof(id));
    internal static FinancePaymentMethod MethodOption(PaymentMethod method)
    {
        var m = Method(method);
        return new(m.Id, m.Label);
    }
    private static (PaymentMethod Method, string Id, string Label, string Color) Method(PaymentMethod method) =>
        Methods.FirstOrDefault(m => m.Method == method && m.Id is not null) is { Id: not null } match
            ? match : (method, method.ToString().ToLowerInvariant(), method.ToString(), "#7a8b89");
    private static Meta Channel(string id) => Channels.FirstOrDefault(c => c.Id == id)
        ?? new(id, id, "IconMore", "#7a8b89");
    private static double Delta(double current, double previous) => previous == 0 ? 0 : (current - previous) / previous * 100;
    private static long Costs(FinanceTotals t) => t.CogsMinor + t.ExpenseMinor;
    private static double Margin(FinanceTotals t) => t.RevenueMinor == 0 ? 0 : (double)t.ProfitMinor / t.RevenueMinor * 100;
    private static FinanceDeltas Deltas(FinanceAggregateReadModel a) => new(
        Delta(a.Current.RevenueMinor, a.Previous.RevenueMinor), Delta(Costs(a.Current), Costs(a.Previous)),
        Delta(a.Current.ProfitMinor, a.Previous.ProfitMinor), Margin(a.Current) - Margin(a.Previous));
    private static IReadOnlyList<FinanceSeriesPoint> Series(FinanceAggregateReadModel a) => a.Series.Select(p =>
        new FinanceSeriesPoint(p.FromDay == p.ToDay ? DayLabel(p.FromDay) : $"{DayLabel(p.FromDay)}–{DayLabel(p.ToDay)}",
            $"{DayLabel(p.FromDay)} تا {DayLabel(p.ToDay)}", p.RevenueMinor, p.ExpenseMinor, p.ProfitMinor)).ToArray();
    private static string DayLabel(int day) => day.ToString("D8", CultureInfo.InvariantCulture) is var text
        ? $"{text[..4]}/{text[4..6]}/{text[6..]}" : "";
    private static IReadOnlyList<FinanceChannel> ToChannels(IReadOnlyList<FinanceChannelReadModel> rows) =>
        rows.Select(r => { var m = Channel(r.Id); return new FinanceChannel(m.Id, m.Label, m.Icon, m.Color, r.RevenueMinor); }).ToArray();
    private static IReadOnlyList<FinanceCategory> ToCategories(IReadOnlyList<FinanceCategoryReadModel> rows) =>
        rows.GroupBy(r => Category(r.CategoryCode).Id).Select(g =>
        {
            var m = Categories.First(c => c.Id == g.Key);
            return new FinanceCategory(m.Id, m.Label, m.Icon, m.Color, m.Tone, g.Sum(r => r.AmountMinor), 0);
        }).OrderByDescending(c => c.Value).ToArray();
    private static IReadOnlyList<FinanceProduct> ToProducts(IReadOnlyList<ProductSalesRow> rows) => rows.Select(r =>
    {
        var m = Channel(r.CategoryId);
        return new FinanceProduct(r.Name, m.Label, r.Kind, m.Icon,
            r.Kind == "service" ? "teal" : "blue", (int)Math.Clamp(decimal.Truncate(r.QuantitySold), 0, int.MaxValue),
            r.RevenueMinor, r.CogsMinor);
    }).ToArray();
    private static IReadOnlyList<FinanceServiceSplit> ProductSplits(IReadOnlyList<FinanceProduct> products) =>
        [new("service", "خدمات و ویزیت", "IconClipboard", "#0d9488",
            products.Where(p => p.Kind == "service").Sum(p => p.Qty), products.Where(p => p.Kind == "service").Sum(p => p.Revenue), products.Where(p => p.Kind == "service").Sum(p => p.Profit)),
         new("product", "فروش کالا و دارو", "IconCart", "#5c8ad6",
            products.Where(p => p.Kind != "service").Sum(p => p.Qty), products.Where(p => p.Kind != "service").Sum(p => p.Revenue), products.Where(p => p.Kind != "service").Sum(p => p.Profit))];
    // Payment-method shares are calculated from the actual projected payment totals where available.
    private static IReadOnlyList<FinancePaymentSplit> Payments(IReadOnlyList<PaymentMethodTotal> rows)
    {
        var total = rows.Sum(r => r.AmountMinor);
        return Methods.Take(4).Select(m => new FinancePaymentSplit(m.Id, m.Label, m.Color,
            total == 0 ? 0 : (int)Math.Round(100.0 * rows.Where(r => r.Method == m.Method).Sum(r => r.AmountMinor) / total))).ToArray();
    }
    internal static FinanceOverview ToContract(this FinanceOverviewReadModel m)
    {
        var a = m.Aggregate; var products = ToProducts(m.Products);
        return new(a.RangeLabel, a.Days, a.Current.RevenueMinor, Costs(a.Current), a.Current.ProfitMinor,
            Margin(a.Current), Deltas(a), a.Days == 0 ? 0 : (double)a.Current.ProfitMinor / a.Days,
            Series(a), ToChannels(m.ByChannel), ToCategories(m.ByCategory), products,
            ProductSplits(products), Insights, Species, [], Heatmap);
    }
    internal static FinanceIncome ToContract(this FinanceIncomeReadModel m)
    {
        var a = m.Aggregate; var channels = ToChannels(m.ByChannel);
        var bars = channels.OrderByDescending(c => c.Value).Select(c => new FinanceChannelBar(
            c.Id, c.Label, c.Icon, c.Color, c.Value,
            a.Current.RevenueMinor == 0 ? 0 : Math.Round(100.0 * c.Value / a.Current.RevenueMinor))).ToArray();
        return new(a.RangeLabel, a.Days, a.Current.RevenueMinor, Costs(a.Current), a.Current.ProfitMinor,
            Margin(a.Current), Deltas(a), a.Days == 0 ? 0 : (double)a.Current.ProfitMinor / a.Days,
            Series(a), channels, [], bars, [], [], Insights, Species, [], Heatmap);
    }
    internal static FinanceExpense ToContract(this FinanceExpenseReadModel m, TimeZoneInfo zone)
    {
        var a = m.Aggregate;
        return new(a.RangeLabel, a.Days, a.Current.RevenueMinor, Costs(a.Current), Deltas(a), Series(a),
            ToCategories(m.ByCategory), m.Rows.Select(r =>
            {
                var cat = Category(r.CategoryCode);
                return new FinanceExpenseRow(r.Id, r.Title, cat.Label, cat.Color,
                    TimeZoneInfo.ConvertTime(r.IncurredUtc, zone).ToString("yyyy/MM/dd", CultureInfo.InvariantCulture),
                    r.Method is { } method ? Method(method).Label : "", r.AmountMinor, r.CreatedByUserId);
            }).ToArray());
    }
    internal static FinanceProfit ToContract(this FinanceProfitReadModel m)
    {
        var a = m.Aggregate;
        return new(a.RangeLabel, a.Days, a.Current.RevenueMinor, Costs(a.Current), a.Current.ProfitMinor,
            Margin(a.Current), Deltas(a), a.Days == 0 ? 0 : (double)a.Current.ProfitMinor / a.Days,
            Series(a), [], ToProducts(m.Products), Insights);
    }
}
