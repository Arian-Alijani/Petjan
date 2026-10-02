using System;
using Animora.Contracts.Appointments;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Animora.Desktop.Pages.Appointments;

/// <summary>
/// One event card on the day/week time grid, mirroring the reference's
/// <c>evCard(a)</c>: the type accent class, status class, tier (xs/sm/md,
/// keyed off computed pixel height), packed lane/width geometry, dim flag and
/// the three per-tier text bodies. Geometry is pre-computed in minutes so the
/// View can place the card without any JS-style math.
/// </summary>
public sealed class AppointmentEventViewModel
{
    public AppointmentEventViewModel(AppointmentEvent model, AppointmentType type, AppointmentDoctor doctor, bool dimmed)
    {
        Model = model;
        Type = type;
        Doctor = doctor;

        var topMinutes = model.StartMinutes - AppointmentsLayout.DayStartMinutes;
        Top = AppointmentsLayout.MinuteToPx(topMinutes) + 1;
        var raw = AppointmentsLayout.MinuteToPx(model.DurationMinutes) - 3;
        Height = Math.Max(raw, AppointmentsLayout.MinEventHeight);
        Tier = Height < 36 ? EventTier.ExtraSmall : Height < 64 ? EventTier.Small : EventTier.Medium;
        IsDimmed = dimmed;
    }

    public AppointmentEvent Model { get; }

    public AppointmentType Type { get; }

    public AppointmentDoctor Doctor { get; }

    /// <summary>Top offset in px from the grid's first hour row.</summary>
    public double Top { get; set; }

    /// <summary>Card min-height in px.</summary>
    public double Height { get; set; }

    public EventTier Tier { get; }

    public bool IsDimmed { get; }

    /// <summary>Assigned lane (0-based) within an overlap cluster.</summary>
    public int Lane { get; set; }

    /// <summary>Total lanes in the overlap cluster.</summary>
    public int Lanes { get; set; } = 1;

    /// <summary>Right inset percentage: <c>lane * (100/lanes)</c>.</summary>
    public double LanePercent => Lane * (100.0 / Lanes);

    /// <summary>Width percentage: <c>100 / lanes</c>.</summary>
    public double WidthPercent => 100.0 / Lanes;

    public string Id => Model.Id;
    public string PetName => Model.PetName;
    public string StatusClass => AppointmentsPresentation.LifecycleClassKey(Model.Lifecycle);
    public string TypeClass => Type.ClassKey;

    public string StartText => AppointmentsPresentation.Time(Model.StartMinutes);
    public string RangeText => AppointmentsPresentation.Range(Model.StartMinutes, Model.EndMinutes);
    public string TypeLabel => Type.Label;
    public string DoctorShort => Doctor.ShortName;
    public string DoctorName => Doctor.Name;
    public string StatusLabel => AppointmentsPresentation.Lifecycle(Model.Lifecycle).Label;
    public bool ShowFlag => Model.Lifecycle != AppointmentLifecycle.Confirmed;
    public bool IsExtraSmall => Tier == EventTier.ExtraSmall;
    public bool IsSmall => Tier == EventTier.Small;
    public bool IsMedium => Tier == EventTier.Medium;
    public bool IsCancelled => Model.Status == AppointmentStatus.Cancelled;

    public IBrush AccentBrush => AppointmentsPresentation.TypeAccent(Type);
    public IBrush BackgroundBrush => AppointmentsPresentation.TypeBackground(Type);
    public IBrush ForegroundBrush => AppointmentsPresentation.TypeForeground(Type);
    public IBrush AvatarBrush => AppointmentsPresentation.AvatarBrush(Model.AvatarTone);
    public IBrush FlagBrush => Model.Lifecycle switch
    {
        AppointmentLifecycle.Scheduled => AppointmentsPresentation.Accent("#b54708"),
        AppointmentLifecycle.CheckedIn or AppointmentLifecycle.Completed => AppointmentsPresentation.Accent("#027a48"),
        AppointmentLifecycle.InVisit => AppointmentsPresentation.Accent("#5a67d8"),
        AppointmentLifecycle.Cancelled or AppointmentLifecycle.NoShow => AppointmentsPresentation.Accent("#d92d20"),
        _ => AppointmentsPresentation.Accent("#98a2b3"),
    };

    public string TierClass => Tier switch
    {
        EventTier.ExtraSmall => "tier-xs",
        EventTier.Small => "tier-sm",
        _ => "tier-md",
    };

    /// <summary>The small-tier secondary text ("· type").</summary>
    public string SmallTypeText => "· " + TypeLabel;

    /// <summary>The medium-tier third line ("type · doctor").</summary>
    public string MediumMetaText => TypeLabel + " · " + DoctorShort;

    /// <summary>The hover tooltip text (pet · type · doctor · start-end · status).</summary>
    public string TooltipText =>
        $"{PetName} · {TypeLabel} · {DoctorName} · " +
        $"{AppointmentsPresentation.Time(Model.StartMinutes)}-" +
        $"{AppointmentsPresentation.Time(Model.EndMinutes)} · {StatusLabel}";
}

/// <summary>Event card density tier, mirroring the reference's tier-* classes.</summary>
public enum EventTier
{
    ExtraSmall,
    Small,
    Medium,
}

/// <summary>
/// One coloured event chip in a month-grid cell, mirroring the reference's
/// <c>mv-ev</c> span (time + pet name).
/// </summary>
public sealed class MonthEventViewModel
{
    public MonthEventViewModel(AppointmentEvent model, AppointmentType type, bool showTime = true)
    {
        Model = model;
        Type = type;
        ShowTime = showTime;
    }

    /// <summary>False in day-only mode, where an appointment has no meaningful clock time.</summary>
    public bool ShowTime { get; }

    public AppointmentEvent Model { get; }
    public AppointmentType Type { get; }
    public string Id => Model.Id;
    public string PetName => Model.PetName;
    public string TimeText => AppointmentsPresentation.Time(Model.StartMinutes);
    public bool IsCancelled => Model.Status == AppointmentStatus.Cancelled;
    public string EventClass => Type.ClassKey;

    /// <summary>Chip background brush for the event type.</summary>
    public IBrush BackgroundBrush => AppointmentsPresentation.TypeBackground(Type);

    /// <summary>Chip foreground (text) brush for the event type.</summary>
    public IBrush ForegroundBrush => AppointmentsPresentation.TypeForeground(Type);

    /// <summary>Chip start-border accent brush for the event type.</summary>
    public IBrush AccentBrush => AppointmentsPresentation.TypeAccent(Type);

    /// <summary>Cancelled month chips dim to 0.55 opacity (reference's <c>.st-cancel</c>).</summary>
    public double ChipOpacity => IsCancelled ? 0.55 : 1;
}

/// <summary>
/// A header cell above a time-grid column: either a doctor (day view — avatar,
/// short name + appointment count) or a calendar day (week view — weekday name
/// + day number), mirroring the reference's <c>tg-hcell</c> variants.
/// </summary>
public sealed class AppointmentColumnHeaderViewModel
{
    public AppointmentColumnHeaderViewModel(AppointmentDoctor doctor, int count)
    {
        IsDay = false;
        Doctor = doctor;
        CountText = AppointmentsPresentation.Fa(count) + " نوبت";
    }

    public AppointmentColumnHeaderViewModel(DateOnly day, bool isToday, bool isFriday)
    {
        IsDay = true;
        Day = day;
        IsToday = isToday;
        IsFriday = isFriday;
        WeekdayText = AppointmentsCalendarMath.DayNames[AppointmentsCalendarMath.WeekIndex(day)];
        DayNumberText = AppointmentsCalendarMath.DayNumber(day);
    }

    public bool IsDay { get; }
    public bool IsDoctor => !IsDay;

    public AppointmentDoctor? Doctor { get; }
    public string CountText { get; } = string.Empty;

    /// <summary>The doctor avatar's accent brush (hex → cached brush).</summary>
    public IBrush AvatarBrush =>
        Doctor is { } d ? AppointmentsPresentation.Accent(d.Accent) : AppointmentsPresentation.Accent("#98a2b3");

    /// <summary>The avatar initial (doctor) or empty (day header).</summary>
    public string Initial => Doctor?.Initial ?? string.Empty;

    /// <summary>The doctor's short display name (empty for a day header).</summary>
    public string ShortName => Doctor?.ShortName ?? string.Empty;

    public DateOnly Day { get; }
    public bool IsToday { get; }
    public bool IsFriday { get; }
    public string WeekdayText { get; } = string.Empty;
    public string DayNumberText { get; } = string.Empty;

    /// <summary>True for the first (right-most, RTL) column's header — suppresses its start border.</summary>
    public bool IsFirst { get; set; }

    /// <summary>Inverse of <see cref="IsToday"/> (drives the plain vs badge day number).</summary>
    public bool IsNotToday => !IsToday;
}

/// <summary>
/// One column of the time grid (a doctor in day view, a day in week view),
/// carrying its header, its packed event cards, the club "closed" flag and the
/// "now" line position (null when the column isn't today).
/// </summary>
public sealed class AppointmentColumnViewModel
{
    public AppointmentColumnViewModel(
        AppointmentColumnHeaderViewModel header,
        IReadOnlyList<AppointmentEventViewModel> events,
        bool isClosed,
        double? nowTop,
        bool isFirst = false)
    {
        Header = header;
        Events = events;
        IsClosed = isClosed;
        NowTop = nowTop;
        HasNow = nowTop.HasValue;
        IsFirst = isFirst;
        NowMargin = new Thickness(0, nowTop ?? 0, 0, 0);
    }

    public AppointmentColumnHeaderViewModel Header { get; }
    public IReadOnlyList<AppointmentEventViewModel> Events { get; }
    public bool IsClosed { get; }
    public double? NowTop { get; }
    public bool HasNow { get; }

    /// <summary>True for the first (right-most, in RTL) column — suppresses its start border.</summary>
    public bool IsFirst { get; }

    /// <summary>The "now" line's top margin (0 when the column has no now line).</summary>
    public Thickness NowMargin { get; }

    /// <summary>The shared "now" line accent brush (#e0564a).</summary>
    public IBrush NowBrush => AppointmentsPresentation.Accent("#e0564a");
}

/// <summary>
/// One cell in the month grid, mirroring the reference's <c>mv-cell</c>: the
/// day number, up to three event chips, an overflow "+ N نوبت دیگر" note and
/// the out-of-month / today / Friday flags.
/// </summary>
public sealed class MonthCellViewModel
{
    public MonthCellViewModel(
        DateOnly day,
        bool isOut,
        bool isToday,
        bool isFriday,
        IReadOnlyList<MonthEventViewModel> events,
        int hiddenCount,
        bool isFirstInRow = false)
    {
        Day = day;
        IsOut = isOut;
        IsToday = isToday;
        IsFriday = isFriday;
        Events = events;
        HiddenCount = hiddenCount;
        HasMore = hiddenCount > 0;
        IsFirstInRow = isFirstInRow;
    }

    public DateOnly Day { get; }
    public bool IsOut { get; }
    public bool IsToday { get; }
    public bool IsFriday { get; }
    public IReadOnlyList<MonthEventViewModel> Events { get; }
    public int HiddenCount { get; }
    public bool HasMore { get; }
    public string DayNumberText => AppointmentsCalendarMath.DayNumber(Day);
    public string MoreText => "+ " + AppointmentsPresentation.Fa(HiddenCount) + " نوبت دیگر";

    /// <summary>True for the first (right-most, RTL) cell in a month row — suppresses its start border.</summary>
    public bool IsFirstInRow { get; }

    /// <summary>Cell border: start separator (0 on the row-leading cell) + bottom rule.</summary>
    public Thickness CellBorderThickness => new(IsFirstInRow ? 0 : 1, 0, 0, 1);

    /// <summary>The out/friday cell background (transparent otherwise), matching the reference.</summary>
    public IBrush CellBackground =>
        IsOut ? AppointmentsPresentation.Accent("#fafbfa")
        : IsFriday ? AppointmentsPresentation.Accent("#fbfcfb")
        : Brushes.Transparent;

    /// <summary>The day-number text brush (muted ink on out-of-month days).</summary>
    public IBrush DayBrush => AppointmentsPresentation.Accent("#344054");

    /// <summary>The out-of-month day number dims to 0.55 opacity.</summary>
    public double DayOpacity => IsOut ? 0.55 : 1;
}

/// <summary>A type filter chip, mirroring the reference's <c>chip t-*</c>.</summary>
public sealed partial class AppointmentTypeChipViewModel : ObservableObject
{
    public AppointmentTypeChipViewModel(AppointmentType type, int count)
    {
        Type = type;
        Count = count;
        CountText = AppointmentsPresentation.Fa(count);
    }

    public AppointmentType Type { get; }
    public int Count { get; }
    public string CountText { get; }
    public string Label => Type.Label;
    public string TypeClass => Type.ClassKey;

    // Selected state takes a soft wash of the type's own accent instead of one global green.
    public IBrush ChipBackground => IsActive
        ? AppointmentsPresentation.SoftFromAccent(Type.Accent)
        : AppointmentsPresentation.AccentFromHex("#FFFFFF");

    public IBrush ChipBorder => IsActive
        ? AppointmentsPresentation.OutlineFromAccent(Type.Accent)
        : AppointmentsPresentation.AccentFromHex("#E5EAEE");

    public IBrush ChipLabel => IsActive
        ? AppointmentsPresentation.ShadeFromAccent(Type.Accent)
        : AppointmentsPresentation.AccentFromHex("#667085");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChipBackground), nameof(ChipBorder), nameof(ChipLabel))]
    private bool _isActive;
}

/// <summary>A doctor filter chip, mirroring the reference's <c>chip doc-chip</c>.</summary>
public sealed partial class AppointmentDoctorChipViewModel : ObservableObject
{
    public AppointmentDoctorChipViewModel(AppointmentDoctor doctor, int count)
    {
        Doctor = doctor;
        Count = count;
        CountText = AppointmentsPresentation.Fa(count);
    }

    public AppointmentDoctor Doctor { get; }
    public int Count { get; }
    public string CountText { get; }
    public string ShortName => Doctor.ShortName;
    public string Initial => Doctor.Initial;

    public IBrush ChipBackground => IsActive
        ? AppointmentsPresentation.SoftFromAccent(Doctor.Accent)
        : AppointmentsPresentation.AccentFromHex("#FFFFFF");

    public IBrush ChipBorder => IsActive
        ? AppointmentsPresentation.OutlineFromAccent(Doctor.Accent)
        : AppointmentsPresentation.AccentFromHex("#E5EAEE");

    public IBrush ChipLabel => IsActive
        ? AppointmentsPresentation.ShadeFromAccent(Doctor.Accent)
        : AppointmentsPresentation.AccentFromHex("#667085");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChipBackground), nameof(ChipBorder), nameof(ChipLabel))]
    private bool _isActive;
}

/// <summary>
/// One appointment in the «نوبت‌های پیش رو» queue column, mirroring the
/// reference's <c>qp-card</c>: index, start/end time, pet avatar, type chip,
/// the live «در حال پذیرش»/«نوبت بعدی» badge and the status pill.
/// </summary>
public sealed class QueueCardViewModel
{
    public QueueCardViewModel(
        AppointmentEvent model,
        AppointmentType type,
        AppointmentDoctor doctor,
        int index,
        QueueLiveBadge live)
    {
        Model = model;
        Type = type;
        Doctor = doctor;
        Index = index;
        Live = live;
    }

    public AppointmentEvent Model { get; }
    public AppointmentType Type { get; }
    public AppointmentDoctor Doctor { get; }
    public int Index { get; }
    public QueueLiveBadge Live { get; }

    public string Id => Model.Id;
    public string IndexText => AppointmentsPresentation.Fa(Index);
    public string StartText => AppointmentsPresentation.Time(Model.StartMinutes);
    public string EndText => AppointmentsPresentation.Time(Model.EndMinutes);
    public string PetName => Model.PetName;
    public string TypeLabel => Type.Label;
    public string TypeClass => Type.ClassKey;
    public string Line2 => $"{Model.PetKind} · مالک: {Model.OwnerName}";
    public string StatusLabel => AppointmentsPresentation.Lifecycle(Model.Lifecycle).Label;
    public string StatusClass => AppointmentsPresentation.LifecycleClassKey(Model.Lifecycle);
    public Tone StatusTone => AppointmentsPresentation.Lifecycle(Model.Lifecycle).Tone;
    public bool IsLiveNow => Live == QueueLiveBadge.Now;
    public bool IsLiveNext => Live == QueueLiveBadge.Next;
}

/// <summary>The live badge state on a queue card (reference's now/next ids).</summary>
public enum QueueLiveBadge
{
    None,
    Now,
    Next,
}

/// <summary>
/// A doctor column in the queue tab, mirroring the reference's <c>qp-doc</c>:
/// avatar + name + specialty header, count, cards and the empty-state flag.
/// </summary>
public sealed class QueueDoctorColumnViewModel
{
    public QueueDoctorColumnViewModel(
        AppointmentDoctor doctor,
        IReadOnlyList<QueueCardViewModel> cards)
    {
        Doctor = doctor;
        Cards = cards;
        CountText = AppointmentsPresentation.Fa(cards.Count);
    }

    public AppointmentDoctor Doctor { get; }
    public IReadOnlyList<QueueCardViewModel> Cards { get; }
    public string CountText { get; }
    public bool IsEmpty => Cards.Count == 0;
    public string ShortName => Doctor.ShortName;
    public string Specialty => Doctor.Specialty;
    public string Initial => Doctor.Initial;
    public string EmptyText => $"برنامهٔ {Doctor.ShortName} در این روز خالی است.";
}

/// <summary>
/// One day button in the queue's 14-day sliding strip, mirroring the
/// reference's <c>qd</c>.
/// </summary>
public sealed partial class QueueDayViewModel : ObservableObject
{
    public QueueDayViewModel(DateOnly day, bool isToday, bool isClosed, int count)
    {
        Day = day;
        IsToday = isToday;
        IsClosed = isClosed;
        Count = count;
    }

    public DateOnly Day { get; }
    public bool IsToday { get; }
    public bool IsClosed { get; }
    public int Count { get; }
    public string DowText => AppointmentsCalendarMath.DayShortNames[AppointmentsCalendarMath.WeekIndex(Day)];
    public string DayText => AppointmentsCalendarMath.DayNumber(Day);
    public string CountText => AppointmentsPresentation.Fa(Count);
    public bool HasCount => Count > 0;

    [ObservableProperty]
    private bool _isActive;
}

/// <summary>
/// One group of history rows sharing a day, mirroring the reference's
/// <c>al-day</c> heading + its <c>hs-row</c> records.
/// </summary>
public sealed class HistoryDayGroupViewModel
{
    public HistoryDayGroupViewModel(DateOnly day, bool isToday, int count, IReadOnlyList<HistoryRowViewModel> rows)
    {
        Day = day;
        IsToday = isToday;
        Count = count;
        Rows = rows;
    }

    public DateOnly Day { get; }
    public bool IsToday { get; }
    public int Count { get; }
    public IReadOnlyList<HistoryRowViewModel> Rows { get; }
    public string DowText => AppointmentsCalendarMath.DayNames[AppointmentsCalendarMath.WeekIndex(Day)] +
                             (IsToday ? " · امروز" : string.Empty);
    public string DateText => AppointmentsCalendarMath.Full(Day);
    public string CountText => AppointmentsPresentation.Fa(Count) + " نوبت";
}

/// <summary>
/// One row in the history archive, mirroring the reference's rich <c>hs-row</c>
/// (time block, avatar, identity line + code + status pill + visit tag, meta
/// line, note, and the financial column with invoice/payment/price).
/// </summary>
public sealed class HistoryRowViewModel
{
    public HistoryRowViewModel(AppointmentEvent model, AppointmentType type, AppointmentDoctor doctor)
    {
        Model = model;
        Type = type;
        Doctor = doctor;
    }

    public AppointmentEvent Model { get; }
    public AppointmentType Type { get; }
    public AppointmentDoctor Doctor { get; }

    public string Id => Model.Id;
    public string StartText => AppointmentsPresentation.Time(Model.StartMinutes);
    public string EndText => AppointmentsPresentation.Time(Model.EndMinutes);
    public string PetName => Model.PetName;
    public string TypeLabel => Type.Label;
    public string TypeClass => Type.ClassKey;
    public string Code => Model.Code;
    public string StatusLabel => AppointmentsPresentation.Lifecycle(Model.Lifecycle).Label;
    public string StatusClass => AppointmentsPresentation.LifecycleClassKey(Model.Lifecycle);
    public Tone StatusTone => AppointmentsPresentation.Lifecycle(Model.Lifecycle).Tone;
    public bool HasVisit => Model.Visit != AppointmentVisitOutcome.None;
    public string VisitLabel => AppointmentsPresentation.Visit(Model.Visit).Label;
    public Tone VisitTone => AppointmentsPresentation.Visit(Model.Visit).Tone;
    public string DoctorShort => Doctor.ShortName;
    public string OwnerName => Model.OwnerName;
    public string PetKind => Model.PetKind;
    public string Phone => Model.OwnerPhone;
    public bool HasNote => !string.IsNullOrWhiteSpace(Model.Note);
    public string Note => Model.Note;
    public string InvoiceLabel => AppointmentsPresentation.Invoice(Model.Invoice).Label;
    public Tone InvoiceTone => AppointmentsPresentation.Invoice(Model.Invoice).Tone;
    public bool HasPayment => Model.Payment != AppointmentPaymentMethod.None;
    public string PaymentLabel => AppointmentsPresentation.Payment(Model.Payment).Label;
    public string PaymentIconKey => AppointmentsPresentation.Payment(Model.Payment).IconKey;
    // A12: only a real linked invoice carries an amount; otherwise show an explicit dash, never a fabricated zero.
    public bool HasPrice => Model.HasInvoice;
    public string PriceText => Model.HasInvoice ? PersianNumberFormatter.FormatGrouped(Model.InvoiceTotalMinor!.Value) : "—";
}

/// <summary>One KPI chip in the queue header (reference's <c>qp-k</c>).</summary>
public sealed record QueueStatViewModel(string Label, string ValueText, Tone Tone);

/// <summary>One KPI box in the history stat band (reference's <c>hs-k</c>).</summary>
public sealed record HistoryStatViewModel(string IconKey, string ValueText, string Label, Tone Tone);

/// <summary>One scope chip in the history toolbar (reference's <c>HSCOPES</c> chips).</summary>
public sealed partial class HistoryScopeChipViewModel : ObservableObject
{
    public HistoryScopeChipViewModel(string id, string label)
    {
        Id = id;
        Label = label;
    }

    public string Id { get; }
    public string Label { get; }

    [ObservableProperty]
    private bool _isActive;
}

/// <summary>One status chip in the history toolbar (reference's <c>HSSTATES</c>).</summary>
public sealed partial class HistoryStatusChipViewModel : ObservableObject
{
    public HistoryStatusChipViewModel(string id, string label)
    {
        Id = id;
        Label = label;
    }

    public string Id { get; }
    public string Label { get; }

    [ObservableProperty]
    private bool _isActive;
}

/// <summary>A compact option for a history toolbar select (doctor/type/invoice).</summary>
public sealed record HistorySelectOption(string Id, string Label);

/// <summary>
/// One day button in the history mini-calendar (reference's <c>hcal-d</c>):
/// carries the out-of-month / today / history-dot / Friday / past / in-range /
/// edge flags that drive its styling, plus the disabled state for future days.
/// </summary>
public sealed partial class HistoryCalendarCellViewModel : ObservableObject
{
    public HistoryCalendarCellViewModel(
        DateOnly day,
        bool isOut,
        bool isToday,
        bool hasHistory,
        bool isFriday,
        bool isPast,
        bool inRange,
        bool edge)
    {
        Day = day;
        IsOut = isOut;
        IsToday = isToday;
        HasHistory = hasHistory;
        IsFriday = isFriday;
        IsPast = isPast;
        InRange = inRange;
        Edge = edge;
    }

    public DateOnly Day { get; }
    public bool IsOut { get; }
    public bool IsToday { get; }
    public bool HasHistory { get; }
    public bool IsFriday { get; }
    public bool IsPast { get; }
    public bool InRange { get; }
    public bool Edge { get; }
    public bool IsDisabled => !IsPast;
    public string DayText => AppointmentsCalendarMath.DayNumber(Day);

    [ObservableProperty]
    private bool _isInactive;
}

/// <summary>
/// One row of the Settings tab's «انواع نوبت» list: the type's colour dot, label and duration.
/// Catalog types are read-only defaults; only clinic-created (custom) types can be removed.
/// </summary>
public sealed class AppointmentSettingTypeRowViewModel
{
    public AppointmentSettingTypeRowViewModel(
        string id, string label, string colorHex, int durationMinutes, bool isCustom, bool? requiresDoctor)
    {
        Id = id;
        Label = label;
        ColorHex = colorHex;
        IsCustom = isCustom;
        var meta = AppointmentsPresentation.Fa(durationMinutes) + " دقیقه";
        if (requiresDoctor is { } needsDoctor)
        {
            meta += needsDoctor ? " · نیاز به پزشک" : " · بدون پزشک";
        }

        MetaText = meta;
    }

    public string Id { get; }
    public string Label { get; }
    public string ColorHex { get; }
    public string MetaText { get; }
    public bool IsCustom { get; }
    public bool CanRemove => IsCustom;
    public string BadgeText => IsCustom ? "سفارشی" : "پیش‌فرض";
}

/// <summary>A numeric choice (minutes / hours) shown in a Settings-tab dropdown.</summary>
public sealed record SettingNumberOption(int Value, string Label);
