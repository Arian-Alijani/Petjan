using Animora.Contracts.Vaccination;
using Animora.Desktop.Localization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Animora.Desktop.Pages.Vaccination;

/// <summary>One kind chip of the calendar filter (ClinicVisit / HomeByOwner / BoardingAdminister) — a toggle.</summary>
public sealed partial class CalendarKindFilterViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public CalendarKindFilterViewModel(TreatmentKind kind)
    {
        Kind = kind;
        Label = VaccinationPresentation.KindShortLabel(kind);
        IconKey = VaccinationPresentation.KindIconKey(kind);
        Brush = VaccinationPresentation.KindBrush(kind);
        Wash = VaccinationPresentation.KindWash(kind);
    }

    public TreatmentKind Kind { get; }
    public string Label { get; }
    public string IconKey { get; }
    public IBrush Brush { get; }
    public IBrush Wash { get; }
}

/// <summary>One option of the item-type filter (همه / واکسن / دارو). <see cref="Type"/> null = no restriction.</summary>
public sealed partial class CalendarItemTypeFilterViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public CalendarItemTypeFilterViewModel(TreatmentItemType? type, string label)
    {
        Type = type;
        Label = label;
    }

    public TreatmentItemType? Type { get; }
    public string Label { get; }
}

/// <summary>A compact event pill inside a month cell, and the row of the selected-day list.</summary>
public sealed partial class CalendarEventViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isDone;

    public CalendarEventViewModel(CalendarOccurrence occurrence)
    {
        Occurrence = occurrence;
        IsDone = occurrence.State == TreatmentOccurrenceState.Done;
        Title = occurrence.Title;
        PetName = occurrence.PetName;
        OwnerName = occurrence.OwnerName;
        PillText = $"{occurrence.PetName} · {occurrence.Title}";
        KindLabel = VaccinationPresentation.KindLabel(occurrence.Kind);
        ItemTypeLabel = VaccinationPresentation.ItemTypeLabel(occurrence.ItemType);
        ItemTypeIconKey = VaccinationPresentation.ItemTypeIconKey(occurrence.ItemType);
        KindIconKey = VaccinationPresentation.KindIconKey(occurrence.Kind);
        Brush = VaccinationPresentation.KindBrush(occurrence.Kind);
        Wash = VaccinationPresentation.KindWash(occurrence.Kind);
        DosageText = occurrence.Dosage ?? string.Empty;
        HasDosage = !string.IsNullOrWhiteSpace(occurrence.Dosage);
        ShowsOwnerReminder = occurrence.NotifyOwner && occurrence.Kind == TreatmentKind.HomeByOwner;
        SpeciesText = VaccinationPresentation.SpeciesLabel(occurrence.Species.ToLowerInvariant());
    }

    public CalendarOccurrence Occurrence { get; }
    public string Title { get; }
    public string PetName { get; }
    public string OwnerName { get; }
    public string PillText { get; }
    public string KindLabel { get; }
    public string KindIconKey { get; }
    public string ItemTypeLabel { get; }
    public string ItemTypeIconKey { get; }
    public string DosageText { get; }
    public bool HasDosage { get; }
    public bool ShowsOwnerReminder { get; }
    public string SpeciesText { get; }
    public IBrush Brush { get; }
    public IBrush Wash { get; }

    /// <summary>The «انجام شد» action is offered only while the day is still pending.</summary>
    public bool CanMarkDone => !IsDone;

    partial void OnIsDoneChanged(bool value) => OnPropertyChanged(nameof(CanMarkDone));
}

/// <summary>One cell of the month grid (a blank lead/trail cell or a real day with up to <c>MaxPills</c> events).</summary>
public sealed partial class CalendarDayViewModel : ObservableObject
{
    public const int MaxPills = 3;

    [ObservableProperty]
    private bool _isSelected;

    public CalendarDayViewModel(DateOnly? day, bool isToday, bool isFriday, IReadOnlyList<CalendarEventViewModel> events)
    {
        Day = day;
        IsBlank = day is null;
        IsToday = isToday;
        IsFriday = isFriday;
        DayText = day is { } d ? AppointmentsDay(d) : string.Empty;
        Events = events;
        Pills = events.Take(MaxPills).ToList();
        var extra = events.Count - Pills.Count;
        HasMore = extra > 0;
        MoreText = extra > 0 ? $"+{PersianNumberFormatter.ToPersianDigits(extra)} مورد دیگر" : string.Empty;
        HasEvents = events.Count > 0;
    }

    public DateOnly? Day { get; }
    public bool IsBlank { get; }
    public bool IsToday { get; }
    public bool IsFriday { get; }
    public string DayText { get; }
    public IReadOnlyList<CalendarEventViewModel> Events { get; }
    public IReadOnlyList<CalendarEventViewModel> Pills { get; }
    public bool HasMore { get; }
    public string MoreText { get; }
    public bool HasEvents { get; }

    private static string AppointmentsDay(DateOnly d) =>
        PersianNumberFormatter.ToPersianDigits(JalaliDate.FromDateTime(d.ToDateTime(TimeOnly.MinValue)).Day);
}

/// <summary>A weekday header of the month grid (شنبه … جمعه).</summary>
public sealed record CalendarWeekdayHeader(string Text, bool IsFriday);
