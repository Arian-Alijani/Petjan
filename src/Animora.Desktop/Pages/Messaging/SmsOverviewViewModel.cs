using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Animora.Contracts.Messaging;
using Animora.Desktop.Controls;
using Animora.Desktop.Pages.Statistics;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Animora.Desktop.Pages.Messaging;

/// <summary>One section (event type) of the usage split: legend row + proportional bar.</summary>
public sealed record SmsSectionUsageRow(string Name, string IconKey, string Count, string Share, double Fraction, IBrush Color);

/// <summary>
/// «نمای کلی» section: remaining credit card, the usage trend (<see cref="BarChart"/>) and the per-section split
/// (<see cref="DonutRing"/> + <see cref="MetricBar"/> legend). The credit shows «در انتظار اتصال» while the provider
/// is unknown (<see cref="SmsCredit.AsOfUtc"/> is null), so the UI never presents a made-up figure.
/// </summary>
public sealed partial class SmsOverviewViewModel(ISmsService service) : SmsSectionViewModel
{
    public override SmsSection Section => SmsSection.Overview;

    public override string Label => "نمای کلی";

    public override string IconKey => "IconDashboard";

    public RangeObservableCollection<SmsSectionUsageRow> Sections { get; } = [];

    [ObservableProperty]
    private IReadOnlyList<BarChartPoint> _trend = [];

    [ObservableProperty]
    private IReadOnlyList<DonutSlice> _sectionSlices = [];

    [ObservableProperty]
    private bool _hasTrend;

    [ObservableProperty]
    private bool _hasSections;

    /// <summary>True once the provider reported a balance (<see cref="SmsCredit.AsOfUtc"/> set).</summary>
    [ObservableProperty]
    private bool _isCreditKnown;

    [ObservableProperty]
    private string _creditValue = "—";

    [ObservableProperty]
    private string _creditFoot = "در انتظار اتصال به درگاه پیامک";

    [ObservableProperty]
    private string _totalSent = "۰";

    [ObservableProperty]
    private string _totalFoot = "پیامکی در این بازه ثبت نشده است";

    [ObservableProperty]
    private string _dailyAverage = "۰";

    [ObservableProperty]
    private string _topSection = "—";

    [ObservableProperty]
    private string _topSectionFoot = string.Empty;

    protected override async Task LoadCoreAsync(SmsReportRange range, CancellationToken ct)
    {
        var creditTask = service.GetCreditAsync(ct);
        var usageTask = service.GetUsageAsync(range, ct);
        await Task.WhenAll(creditTask, usageTask);
        var credit = creditTask.Result;
        var usage = usageTask.Result;
        await PublishOnUiAsync(() => Publish(credit, usage));
    }

    protected override void ReleaseHeavyData()
    {
        Sections.Clear();
        Trend = [];
        SectionSlices = [];
    }

    private void Publish(SmsCredit credit, SmsUsageReport usage)
    {
        IsCreditKnown = credit.AsOfUtc is not null;
        if (credit.AsOfUtc is { } asOf)
        {
            CreditValue = $"{SmsFormat.Count(credit.RemainingCount)} پیامک";
            var updated = $"به‌روزرسانی: {SmsFormat.JalaliDateTime(asOf)}";
            CreditFoot = credit.RemainingCreditMinor > 0
                ? $"{StatisticsFormat.Money(credit.RemainingCreditMinor)} · {updated}"
                : updated;
        }
        else
        {
            CreditValue = "—";
            CreditFoot = "در انتظار اتصال به درگاه پیامک";
        }

        Trend = usage.Trend
            .OrderBy(p => p.Day)
            .Select(p => new BarChartPoint(p.Count, SmsFormat.JalaliDay(p.Day),
                $"{SmsFormat.JalaliDayFull(p.Day)}: {SmsFormat.Count(p.Count)} پیامک"))
            .ToArray();
        HasTrend = Trend.Count > 0 && usage.Trend.Any(p => p.Count > 0);

        var positive = usage.BySection.Where(s => s.Count > 0).OrderByDescending(s => s.Count).ToArray();
        long sectionTotal = positive.Sum(s => (long)s.Count);
        SectionSlices = positive.Select(s => new DonutSlice(s.Count, SmsFormat.EventColor(s.EventType))).ToArray();
        Sections.ReplaceAll(positive.Select(s => new SmsSectionUsageRow(
            SmsFormat.EventName(s.EventType),
            SmsFormat.EventIcon(s.EventType),
            SmsFormat.Count(s.Count),
            StatisticsFormat.Percent(s.Count, sectionTotal),
            sectionTotal <= 0 ? 0 : s.Count / (double)sectionTotal,
            StatisticsFormat.Brush(SmsFormat.EventColor(s.EventType)))));
        HasSections = positive.Length > 0;

        TotalSent = SmsFormat.Count(usage.TotalCount);
        TotalFoot = usage.TotalCount == 0 ? "پیامکی در این بازه ثبت نشده است" : "مجموع پیامک‌های این بازه";
        var days = Math.Max(1, usage.Trend.Count);
        DailyAverage = SmsFormat.Count((long)Math.Round(usage.TotalCount / (double)days, MidpointRounding.AwayFromZero));
        var top = positive.FirstOrDefault();
        TopSection = top is null ? "—" : SmsFormat.EventName(top.EventType);
        TopSectionFoot = top is null ? "هنوز مصرفی ثبت نشده است" : $"{SmsFormat.Count(top.Count)} پیامک";
    }
}
