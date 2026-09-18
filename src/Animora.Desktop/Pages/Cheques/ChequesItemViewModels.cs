using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Animora.Contracts.Cheques;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Animora.Desktop.Pages.Cheques;

/// <summary>
/// The four render modes of the cheque list, mirroring the reference's
/// <c>CH.view</c> (<c>table | grid | calendar | timeline</c>).
/// </summary>
public enum ChequeViewMode
{
    Table,
    Grid,
    Calendar,
    Timeline,
}

/// <summary>One direction tab of the toolbar (<c>renderChips</c>'s <c>#chTabs</c>).</summary>
public sealed partial class ChequeTabViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private string _countText = string.Empty;

    public ChequeTabViewModel(string key, string label, string? iconKey)
    {
        Key = key;
        Label = label;
        IconKey = iconKey;
    }

    /// <summary>all | in | out — the reference's <c>data-tab</c>.</summary>
    public string Key { get; }

    public string Label { get; }

    /// <summary>Null for the «همه چک‌ها» tab (no leading icon).</summary>
    public string? IconKey { get; }

    public bool HasIcon => IconKey is not null;
}

/// <summary>One icon-only render-mode button of the view switch (<c>#chViews</c>).</summary>
public sealed partial class ChequeViewModeViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isActive;

    public ChequeViewModeViewModel(ChequeViewMode mode, string iconKey, string tooltip)
    {
        Mode = mode;
        IconKey = iconKey;
        Tooltip = tooltip;
    }

    public ChequeViewMode Mode { get; }

    public string IconKey { get; }

    public string Tooltip { get; }
}

/// <summary>
/// One status filter chip of the tier-2 rail (<c>renderChips</c>): the status,
/// its label, live count, per-status accent color and active state.
/// </summary>
public sealed partial class ChequeStatusChipViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private string _countText = string.Empty;

    public ChequeStatusChipViewModel(ChequeStatus status, string label, string desc, string color)
    {
        Status = status;
        Label = label;
        Desc = desc;
        Color = color;
    }

    public ChequeStatus Status { get; }

    public string Label { get; }

    public string Desc { get; }

    public string Color { get; }

    public IBrush DotBrush => ChequesPresentation.Fill(Color);
}

/// <summary>
/// The KPI strip's four cards (<c>renderKpis</c>): the accent color, icon,
/// label, count, short value, a foot line split into plain / highlighted parts,
/// and a mini sparkline rendered as line + area geometry.
/// </summary>
public sealed class ChequeKpiViewModel
{
    public ChequeKpiViewModel(
        int index, string iconKey, string label, int count, long value,
        string footPrefix, string footHighlight, string footSuffix, string footHighlightTone,
        IReadOnlyList<double> spark)
    {
        Index = index;
        IconKey = iconKey;
        Label = label;
        CountText = $"{ChequesPresentation.Fa(count)} فقره";
        ValueText = ChequesPresentation.MoneyShort(value);
        FootPrefix = footPrefix;
        FootHighlight = footHighlight;
        FootSuffix = footSuffix;
        HasFootHighlight = !string.IsNullOrEmpty(footHighlight);
        FootHighlightBrush = ChequesPresentation.Fill(footHighlightTone);
        AccentBrush = ChequesPresentation.KpiForeground(index);
        IconTileBrush = ChequesPresentation.KpiTileBackground(index);
        Color = ChequesPresentation.KpiColor(index);

        var (line, area) = BuildSpark(spark);
        SparkLine = line;
        SparkArea = area;
        SparkStroke = ChequesPresentation.Fill(Color);
        SparkFill = BuildSparkFill(Color);
    }

    /// <summary>0..3 — the reference's <c>data-kpi</c> index (drives the click-through filter).</summary>
    public int Index { get; }

    public string IconKey { get; }

    public string Label { get; }

    public string CountText { get; }

    public string ValueText { get; }

    public string FootPrefix { get; }

    public string FootHighlight { get; }

    public string FootSuffix { get; }

    public bool HasFootHighlight { get; }

    public IBrush FootHighlightBrush { get; }

    public string Color { get; }

    public IBrush AccentBrush { get; }

    public IBrush IconTileBrush { get; }

    public Geometry? SparkLine { get; }

    public Geometry? SparkArea { get; }

    public IBrush SparkStroke { get; }

    public IBrush SparkFill { get; }

    // The reference viewBox is 240×40 with a 4px/12px vertical inset; the path
    // is normalized to the min/max of the series (sparkSvg).
    private static (Geometry? Line, Geometry? Area) BuildSpark(IReadOnlyList<double> vals)
    {
        if (vals.Count < 2)
        {
            return (null, null);
        }

        const double w = 240;
        const double h = 40;
        var max = vals.Max();
        var min = vals.Min();
        var range = (max - min) == 0 ? 1 : max - min;

        var pts = new Point[vals.Count];
        for (var i = 0; i < vals.Count; i++)
        {
            var x = i * (w / (vals.Count - 1));
            var y = h - 4 - ((vals[i] - min) / range * (h - 12));
            pts[i] = new Point(x, y);
        }

        var line = new PathGeometry();
        var lineFig = new PathFigure { StartPoint = pts[0], IsClosed = false, IsFilled = false };
        for (var i = 1; i < pts.Length; i++)
        {
            lineFig.Segments!.Add(new LineSegment { Point = pts[i] });
        }

        line.Figures!.Add(lineFig);

        var area = new PathGeometry();
        var areaFig = new PathFigure { StartPoint = pts[0], IsClosed = true, IsFilled = true };
        for (var i = 1; i < pts.Length; i++)
        {
            areaFig.Segments!.Add(new LineSegment { Point = pts[i] });
        }

        areaFig.Segments!.Add(new LineSegment { Point = new Point(w, h) });
        areaFig.Segments!.Add(new LineSegment { Point = new Point(0, h) });
        area.Figures!.Add(areaFig);

        return (line, area);
    }

    private static IBrush BuildSparkFill(string color) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Avalonia.Media.Color.Parse(color), 0),
            new GradientStop(Avalonia.Media.Color.Parse("#00FFFFFF"), 1),
        },
    };
}

/// <summary>
/// One status-pill view instance (the reference's <c>statusPill</c>) — label,
/// icon key and tone brushes.
/// </summary>
public sealed class ChequeStatusPillViewModel
{
    public ChequeStatusPillViewModel(ChequeStatus status)
    {
        Label = ChequesPresentation.StatusLabel(status);
        IconKey = ChequesPresentation.StatusIcon(status);
        var (bg, fg) = ChequesPresentation.StatusPill(status);
        Background = bg;
        Foreground = fg;
    }

    public string Label { get; }

    public string IconKey { get; }

    public IBrush Background { get; }

    public IBrush Foreground { get; }
}

/// <summary>
/// A due-cell countdown pill (the reference's <c>cd</c>): the class-derived
/// tone brushes, its icon and its text.
/// </summary>
public sealed class ChequeCountdownViewModel
{
    public ChequeCountdownViewModel(string cls, string text, string iconKey)
    {
        Text = text;
        IconKey = iconKey;
        IsNow = cls == "now";
        var (bg, fg) = ChequesPresentation.CountdownPill(cls);
        Background = bg;
        Foreground = fg;
    }

    public string Text { get; }

    public string IconKey { get; }

    public bool IsNow { get; }

    public IBrush Background { get; }

    public IBrush Foreground { get; }
}

/// <summary>
/// One product/party bank mark (the reference's <c>bankMark</c>): the gradient
/// tile, its short label and an optional size.
/// </summary>
public sealed class ChequeBankMarkViewModel
{
    public ChequeBankMarkViewModel(ChequeBank bank, double size = 26)
    {
        ShortLabel = bank.Short.Length > 3 ? bank.Short[..3] : bank.Short;
        Brush = ChequesPresentation.BankGradient(bank.ColorFrom, bank.ColorTo);
        Size = size;
    }

    public string ShortLabel { get; }

    public IBrush Brush { get; }

    public double Size { get; }

    public double CornerRadius => Size >= 34 ? 11 : Size >= 30 ? 10 : 9;
}

/// <summary>One table row of the cheque list (<c>renderTable</c>).</summary>
public sealed partial class ChequeRowViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>The row's "more" menu popup open state (<c>openRowMenu</c>).</summary>
    [ObservableProperty]
    private bool _isMenuOpen;

    public ChequeRowViewModel(Cheque c, ChequeBank bank, int daysLeft)
    {
        Id = c.Id;
        Amount = c.Amount;
        NumberText = ChequesPresentation.FaText(c.Number);
        SayadShort = ChequesPresentation.FaText(c.Sayad[..Math.Min(9, c.Sayad.Length)]) + "…";
        Party = c.Party;
        Role = c.Role;
        Initials = ChequesPresentation.Initials(c.Party);
        AvatarBrush = ChequesPresentation.AvatarGradient(c.Party);

        Direction = c.Direction;
        DirectionLabel = ChequesPresentation.DirectionLabel(c.Direction);
        DirectionIcon = ChequesPresentation.DirectionIcon(c.Direction);
        var (dirBg, dirFg) = ChequesPresentation.DirectionTag(c.Direction);
        DirectionTagBackground = dirBg;
        DirectionTagForeground = dirFg;

        BankMark = new ChequeBankMarkViewModel(bank);
        BankName = bank.Name;
        Branch = c.Branch;

        AmountText = ChequesPresentation.Money(c.Amount);
        AmountForeground = ChequesPresentation.AmountForeground(c.Direction);

        DueText = ChequesPresentation.FaText(c.DueDate);

        var cls = ChequesPresentation.CountdownClass(c.Status, daysLeft);
        Countdown = new ChequeCountdownViewModel(
            cls,
            ChequesPresentation.CountdownText(c.Status, daysLeft),
            ChequesPresentation.CountdownIcon(c.Status, daysLeft));

        Status = new ChequeStatusPillViewModel(c.Status);

        MenuActions = BuildMenu(c);
    }

    /// <summary>
    /// The row's "more" menu entries, mirroring <c>openRowMenu</c> — the base
    /// entries plus the status-specific block only while the cheque is open.
    /// </summary>
    private static IReadOnlyList<ChequeMenuActionViewModel> BuildMenu(Cheque c)
    {
        var open = c.Status is ChequeStatus.InHand or ChequeStatus.AtBank;
        var actions = new List<ChequeMenuActionViewModel>
        {
            new(c.Id, "open", "مشاهده جزئیات", "IconFile", false),
            new(c.Id, "edit", "ویرایش اطلاعات", "IconEdit", false),
            new(c.Id, "copy", "کپی شناسه صیادی", "IconCopy", false),
        };

        if (open)
        {
            actions.Add(new ChequeMenuActionViewModel(c.Id, "bank", "واگذاری به بانک", "IconBank", false, true));
            actions.Add(new ChequeMenuActionViewModel(c.Id, "cleared", "ثبت وصول", "IconCheck", false));
            actions.Add(new ChequeMenuActionViewModel(c.Id, "bounced", "ثبت برگشت", "IconAlert", false));
            if (c.Direction == ChequeDirection.In)
            {
                actions.Add(new ChequeMenuActionViewModel(c.Id, "spent", "خرج کردن چک", "IconShare", false));
            }
        }

        actions.Add(new ChequeMenuActionViewModel(c.Id, "print", "چاپ رسید", "IconPrinter", false, true));
        actions.Add(new ChequeMenuActionViewModel(c.Id, "sms", $"یادآوری به {c.Party}", "IconSms", false));
        actions.Add(new ChequeMenuActionViewModel(c.Id, "del", "حذف چک", "IconTrash", true, true));
        return actions;
    }

    /// <summary>The row's "more" menu entries (<c>openRowMenu</c>).</summary>
    public IReadOnlyList<ChequeMenuActionViewModel> MenuActions { get; }

    public string Id { get; }

    /// <summary>The raw amount (used by the bulk-sum line).</summary>
    public long Amount { get; }

    public string NumberText { get; }

    public string SayadShort { get; }

    public string Party { get; }

    public string Role { get; }

    public string Initials { get; }

    public IBrush AvatarBrush { get; }

    public ChequeDirection Direction { get; }

    public string DirectionLabel { get; }

    public string DirectionIcon { get; }

    public IBrush DirectionTagBackground { get; }

    public IBrush DirectionTagForeground { get; }

    public ChequeBankMarkViewModel BankMark { get; }

    public string BankName { get; }

    public string Branch { get; }

    public string AmountText { get; }

    public IBrush AmountForeground { get; }

    public string DueText { get; }

    public ChequeCountdownViewModel Countdown { get; }

    public ChequeStatusPillViewModel Status { get; }
}

/// <summary>One leaf card of the grid render mode (<c>renderGrid</c>).</summary>
public sealed class ChequeLeafViewModel
{
    public ChequeLeafViewModel(Cheque c, ChequeBank bank, int daysLeft)
    {
        Id = c.Id;
        BankMarkLabel = bank.Short.Length > 3 ? bank.Short[..3] : bank.Short;
        BankName = bank.Name;
        Branch = c.Branch;
        DirectionLabel = ChequesPresentation.DirectionLabel(c.Direction);
        BankFrom = bank.ColorFrom;
        BankTo = bank.ColorTo;

        AmountText = ChequesPresentation.Money(c.Amount);
        WordsText = ChequesPresentation.NumberToWords(c.Amount) + " تومان";

        PartyKey = c.Direction == ChequeDirection.In ? "در وجه / از" : "در وجه / از";
        Party = c.Party;
        DueText = ChequesPresentation.FaText(c.DueDate);

        NumberText = ChequesPresentation.FaText(c.Number);

        var cls = ChequesPresentation.CountdownClass(c.Status, daysLeft);
        Countdown = new ChequeCountdownViewModel(
            cls,
            ChequesPresentation.CountdownText(c.Status, daysLeft),
            ChequesPresentation.CountdownIcon(c.Status, daysLeft));

        Status = new ChequeStatusPillViewModel(c.Status);
    }

    public string Id { get; }

    public string BankMarkLabel { get; }

    public string BankName { get; }

    public string Branch { get; }

    public string DirectionLabel { get; }

    public string BankFrom { get; }

    public string BankTo { get; }

    public IBrush HeaderBrush => ChequesPresentation.BankGradient(BankFrom, BankTo);

    public string AmountText { get; }

    public string WordsText { get; }

    public string PartyKey { get; }

    public string Party { get; }

    public string DueText { get; }

    public string NumberText { get; }

    public ChequeCountdownViewModel Countdown { get; }

    public ChequeStatusPillViewModel Status { get; }
}

/// <summary>One event chip inside a calendar day (<c>renderCalendar</c>'s <c>.cal-ev</c>).</summary>
public sealed class ChequeCalendarEventViewModel
{
    public ChequeCalendarEventViewModel(Cheque c)
    {
        Id = c.Id;
        AmountText = ChequesPresentation.MoneyShort(c.Amount);
        Tooltip = $"{c.Party} — {ChequesPresentation.Money(c.Amount)} تومان";
        if (c.Direction == ChequeDirection.In)
        {
            Background = ChequesPresentation.Fill("#e6f2ec");
            Foreground = ChequesPresentation.Fill("#1d6b4d");
            Accent = ChequesPresentation.Fill("#027a48");
        }
        else
        {
            Background = ChequesPresentation.Fill("#fbeade");
            Foreground = ChequesPresentation.Fill("#a95a22");
            Accent = ChequesPresentation.Fill("#e08a5c");
        }
    }

    public string Id { get; }

    public string AmountText { get; }

    public string Tooltip { get; }

    public IBrush Background { get; }

    public IBrush Foreground { get; }

    public IBrush Accent { get; }
}

/// <summary>One day cell of the calendar render mode (<c>renderCalendar</c>'s <c>.cal-cell</c>).</summary>
public sealed class ChequeCalendarCellViewModel
{
    public ChequeCalendarCellViewModel(int day, bool isOff, bool isToday, bool isFriday, long dayTotal, IReadOnlyList<ChequeCalendarEventViewModel> events)
    {
        DayText = ChequesPresentation.Fa(day);
        IsOff = isOff;
        IsToday = isToday;
        IsFriday = isFriday;
        SumText = dayTotal > 0 ? ChequesPresentation.MoneyShort(dayTotal) : string.Empty;
        Events = events;
        MoreText = events.Count > 2 ? $"+{ChequesPresentation.Fa(events.Count - 2)} مورد دیگر" : string.Empty;
    }

    public string DayText { get; }

    public bool IsOff { get; }

    public bool IsToday { get; }

    public bool IsFriday { get; }

    public string SumText { get; }

    public bool HasSum => SumText.Length > 0;

    public IReadOnlyList<ChequeCalendarEventViewModel> Events { get; }

    public IReadOnlyList<ChequeCalendarEventViewModel> VisibleEvents => Events.Take(2).ToList();

    public string MoreText { get; }

    public bool HasMore => MoreText.Length > 0;

    public IBrush Background => IsToday
        ? ChequesPresentation.Fill(ChequesPresentation.Mix("#14b8a6", "#ffffff", 0.07))
        : IsOff
            ? ChequesPresentation.Fill("#fcfdfc")
            : ChequesPresentation.Fill("#ffffff");
}

/// <summary>One item row of a timeline group (<c>renderTimeline</c>'s <c>.tl-item</c>).</summary>
public sealed class ChequeTimelineItemViewModel
{
    public ChequeTimelineItemViewModel(Cheque c, ChequeBank bank, int daysLeft)
    {
        Id = c.Id;
        BankMark = new ChequeBankMarkViewModel(bank);
        Party = c.Party;
        SubText = $"{ChequesPresentation.FaText(c.DueDate)} · " +
                  $"{ChequesPresentation.FaText(c.Number)} · {bank.Name}";
        DirectionLabel = ChequesPresentation.DirectionLabel(c.Direction);
        DirectionIcon = ChequesPresentation.DirectionIcon(c.Direction);
        var (dirBg, dirFg) = ChequesPresentation.DirectionTag(c.Direction);
        DirectionTagBackground = dirBg;
        DirectionTagForeground = dirFg;
        AmountText = ChequesPresentation.Money(c.Amount);
        AmountForeground = ChequesPresentation.AmountForeground(c.Direction);
        Countdown = new ChequeCountdownViewModel(
            ChequesPresentation.CountdownClass(c.Status, daysLeft),
            ChequesPresentation.CountdownText(c.Status, daysLeft),
            ChequesPresentation.CountdownIcon(c.Status, daysLeft));
    }

    public string Id { get; }

    public ChequeBankMarkViewModel BankMark { get; }

    public string Party { get; }

    public string SubText { get; }

    public string DirectionLabel { get; }

    public string DirectionIcon { get; }

    public IBrush DirectionTagBackground { get; }

    public IBrush DirectionTagForeground { get; }

    public string AmountText { get; }

    public IBrush AmountForeground { get; }

    public ChequeCountdownViewModel Countdown { get; }
}

/// <summary>One bucket group of the timeline render mode (<c>renderTimeline</c>'s <c>.tl-group</c>).</summary>
public sealed class ChequeTimelineGroupViewModel
{
    public ChequeTimelineGroupViewModel(string title, string tone, long sum, IReadOnlyList<ChequeTimelineItemViewModel> items)
    {
        Title = title;
        CountText = ChequesPresentation.Fa(items.Count);
        SumText = ChequesPresentation.MoneyShort(sum) + " تومان";
        Items = items;
        DotBrush = ChequesPresentation.Fill(tone switch
        {
            "warn" => "#b54708",
            "danger" => "#d92d20",
            _ => "#14b8a6",
        });
    }

    public string Title { get; }

    public string CountText { get; }

    public string SumText { get; }

    public IReadOnlyList<ChequeTimelineItemViewModel> Items { get; }

    public IBrush DotBrush { get; }
}

/// <summary>One week bar column of the cash-flow side card (<c>cardCashflow</c>).</summary>
public sealed class ChequeCashflowWeekViewModel
{
    public ChequeCashflowWeekViewModel(string label, long inValue, long outValue, double max)
    {
        Label = label;
        FirstWord = label.Split(' ')[0];
        InBarHeight = max <= 0 ? 0 : inValue / max * 46 / 100 * 84;
        OutBarHeight = max <= 0 ? 0 : outValue / max * 46 / 100 * 84;
        TipText = $"{label}\nدریافتی: {ChequesPresentation.MoneyShort(inValue)}\nپرداختی: {ChequesPresentation.MoneyShort(outValue)}";
    }

    public string Label { get; }

    public string FirstWord { get; }

    public double InBarHeight { get; }

    public double OutBarHeight { get; }

    public string TipText { get; }
}

/// <summary>The cash-flow side card (<c>cardCashflow</c>).</summary>
public sealed class ChequeCashflowViewModel
{
    public ChequeCashflowViewModel(CashflowSummary summary)
    {
        var max = Math.Max(1, summary.Weeks.Select(w => Math.Max(w.In, w.Out)).DefaultIfEmpty(1).Max());
        Weeks = summary.Weeks.Select(w => new ChequeCashflowWeekViewModel(w.Label, w.In, w.Out, max)).ToList();

        var netIn = summary.Weeks.Sum(w => w.In);
        var netOut = summary.Weeks.Sum(w => w.Out);
        var net = netIn - netOut;

        NetPositive = net >= 0;
        NetText = (net >= 0 ? "+" : "−") + ChequesPresentation.MoneyShort(Math.Abs(net));
        var (bg, fg) = net >= 0
            ? (ChequesPresentation.Fill("#ecfdf3"), ChequesPresentation.Fill("#027a48"))
            : (ChequesPresentation.Fill("#fef3f2"), ChequesPresentation.Fill("#d92d20"));
        NetBackground = bg;
        NetForeground = fg;
        NetIcon = net >= 0 ? "IconTrend" : "IconAlert";

        InLegendText = $"دریافتی {ChequesPresentation.MoneyShort(netIn)}";
        OutLegendText = $"پرداختی {ChequesPresentation.MoneyShort(netOut)}";
    }

    public IReadOnlyList<ChequeCashflowWeekViewModel> Weeks { get; }

    public bool NetPositive { get; }

    public string NetText { get; }

    public string NetIcon { get; }

    public IBrush NetBackground { get; }

    public IBrush NetForeground { get; }

    public string InLegendText { get; }

    public string OutLegendText { get; }
}

/// <summary>One bank row of the «توزیع بانکی» side card (<c>cardBanks</c>).</summary>
public sealed partial class ChequeBankRowViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isActive;

    public ChequeBankRowViewModel(ChequeBank bank, long amount, int count, double max)
    {
        BankKey = bank.Key;
        Name = bank.Name;
        CountText = $"{ChequesPresentation.Fa(count)} فقره";
        ValueText = ChequesPresentation.MoneyShort(amount);
        TrackWidth = max <= 0 ? 0 : amount / max * 100;
        TrackBrush = ChequesPresentation.BankTrackGradient(bank.ColorFrom, bank.ColorTo);
        Mark = new ChequeBankMarkViewModel(bank);
    }

    public string BankKey { get; }

    public string Name { get; }

    public string CountText { get; }

    public string ValueText { get; }

    public double TrackWidth { get; }

    public IBrush TrackBrush { get; }

    public ChequeBankMarkViewModel Mark { get; }
}

/// <summary>One alert / action item of the side column (<c>cardAlerts</c>).</summary>
public sealed class ChequeAlertViewModel
{
    public ChequeAlertViewModel(ChequeAlert alert)
    {
        Kind = alert.Kind;
        Title = alert.Title;
        Detail = alert.Detail;
        IconKey = ChequesPresentation.AlertIcon(alert.Kind);
        var (bg, fg) = Tones(alert.Kind);
        IconBackground = bg;
        IconForeground = fg;
    }

    public ChequeAlertKind Kind { get; }

    public string Title { get; }

    public string Detail { get; }

    public string IconKey { get; }

    public IBrush IconBackground { get; }

    public IBrush IconForeground { get; }

    private static (IBrush Background, IBrush Foreground) Tones(ChequeAlertKind kind)
    {
        var color = kind switch
        {
            ChequeAlertKind.SoonOut => "#b54708",
            ChequeAlertKind.Sms => "#027a48",
            _ => "#d92d20",
        };
        return (ChequesPresentation.Fill(ChequesPresentation.Mix(color, "#ffffff", 0.12)), ChequesPresentation.Fill(color));
    }
}

/// <summary>One page button of the table pager (<c>pager</c>).</summary>
public sealed class ChequePagerButtonViewModel
{
    public ChequePagerButtonViewModel(string label, int page, bool isActive, bool isEllipsis, bool isEnabled)
    {
        Label = label;
        Page = page;
        IsActive = isActive;
        IsEllipsis = isEllipsis;
        IsEnabled = isEnabled;
    }

    public string Label { get; }

    public int Page { get; }

    public bool IsActive { get; }

    public bool IsEllipsis { get; }

    public bool IsEnabled { get; }
}

/// <summary>The table pager state (<c>pager</c>).</summary>
public sealed class ChequePagerViewModel
{
    public ChequePagerViewModel(string text, IReadOnlyList<ChequePagerButtonViewModel> pages, bool hasPrev, bool hasNext, int prevPage, int nextPage)
    {
        Text = text;
        Pages = pages;
        HasPrev = hasPrev;
        HasNext = hasNext;
        PrevPage = prevPage;
        NextPage = nextPage;
    }

    public string Text { get; }

    public IReadOnlyList<ChequePagerButtonViewModel> Pages { get; }

    public bool HasPrev { get; }

    public bool HasNext { get; }

    public int PrevPage { get; }

    public int NextPage { get; }
}

/// <summary>One clickable entry of the row "more" menu (<c>openRowMenu</c>).</summary>
public sealed class ChequeMenuActionViewModel
{
    public ChequeMenuActionViewModel(string chequeId, string actionKey, string label, string iconKey, bool isDanger, bool showTopSeparator = false)
    {
        ChequeId = chequeId;
        ActionKey = actionKey;
        Label = label;
        IconKey = iconKey;
        IsDanger = isDanger;
        ShowTopSeparator = showTopSeparator;
    }

    public string ChequeId { get; }

    public string ActionKey { get; }

    public string Label { get; }

    public string IconKey { get; }

    public bool IsDanger { get; }

    /// <summary>Whether a hairline divider sits above this entry (the reference's <c>.sepline</c>).</summary>
    public bool ShowTopSeparator { get; }
}

/// <summary>
/// The row "more" menu (<c>openRowMenu</c>): its title, its action entries and
/// its separator positions.
/// </summary>
public sealed class ChequeRowMenuViewModel
{
    public ChequeRowMenuViewModel(string chequeId, string title, IReadOnlyList<ChequeMenuActionViewModel> actions)
    {
        ChequeId = chequeId;
        Title = title;
        Actions = actions;
    }

    public string ChequeId { get; }

    public string Title { get; }

    public IReadOnlyList<ChequeMenuActionViewModel> Actions { get; }
}
