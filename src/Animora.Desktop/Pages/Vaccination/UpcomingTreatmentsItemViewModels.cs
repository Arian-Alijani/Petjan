using Animora.Contracts.Vaccination;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Pages.Appointments;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Animora.Desktop.Pages.Vaccination;

/// <summary>One day button of the upcoming tab's 14-day strip (count = pending items that day).</summary>
public sealed partial class UpcomingDayViewModel : ObservableObject
{
    public UpcomingDayViewModel(DateOnly day, bool isToday, bool isClosed, int pendingCount)
    {
        Day = day;
        IsToday = isToday;
        IsClosed = isClosed;
        CountText = pendingCount > 0 ? VaccinationPresentation.Fa(pendingCount) : "—";
    }

    public DateOnly Day { get; }
    public bool IsToday { get; }
    public bool IsClosed { get; }
    public string DowText => AppointmentsCalendarMath.DayShortNames[AppointmentsCalendarMath.WeekIndex(Day)];
    public string DayText => AppointmentsCalendarMath.DayNumber(Day);
    public string CountText { get; }

    [ObservableProperty]
    private bool _isActive;
}

/// <summary>One KPI chip of the upcoming tab's header.</summary>
public sealed record UpcomingStatViewModel(string Label, string ValueText, Tone Tone);

/// <summary>One card of a kind column: a scheduled vaccine / medication day. Opens the shared detail popup.</summary>
public sealed class UpcomingCardViewModel
{
    public UpcomingCardViewModel(CalendarEventViewModel item, int index)
    {
        Event = item;
        IndexText = VaccinationPresentation.Fa(index);
        Line2 = item.HasDosage
            ? $"{item.Title} · {item.DosageText} · مالک: {item.OwnerName}"
            : $"{item.Title} · مالک: {item.OwnerName}";
        StatusLabel = item.IsDone ? "انجام‌شده" : "در انتظار";
        StatusTone = item.IsDone ? Tone.Ok : Tone.Warn;
    }

    public CalendarEventViewModel Event { get; }
    public string IndexText { get; }
    public string PetName => Event.PetName;
    public string TypeLabel => Event.ItemTypeLabel;
    public string TypeIconKey => Event.ItemTypeIconKey;
    public IBrush Brush => Event.Brush;
    public string Line2 { get; }
    public string StatusLabel { get; }
    public Tone StatusTone { get; }
}

/// <summary>One column of the upcoming tab — the day's items of a single plan kind (clinic / home / boarding).</summary>
public sealed class UpcomingKindColumnViewModel
{
    public UpcomingKindColumnViewModel(TreatmentKind kind, IReadOnlyList<UpcomingCardViewModel> cards)
    {
        Cards = cards;
        Label = VaccinationPresentation.KindLabel(kind);
        IconKey = VaccinationPresentation.KindIconKey(kind);
        Brush = VaccinationPresentation.KindBrush(kind);
        Wash = VaccinationPresentation.KindWash(kind);
        CountText = VaccinationPresentation.Fa(cards.Count);
        EmptyText = $"برای «{VaccinationPresentation.KindShortLabel(kind)}» در این روز موردی برنامه‌ریزی نشده است.";
    }

    public IReadOnlyList<UpcomingCardViewModel> Cards { get; }
    public string Label { get; }
    public string IconKey { get; }
    public IBrush Brush { get; }
    public IBrush Wash { get; }
    public string CountText { get; }
    public string EmptyText { get; }
    public bool IsEmpty => Cards.Count == 0;
}
