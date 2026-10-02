using System.Collections.Generic;
using Animora.Contracts.Appointments;
using Animora.Desktop.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Animora.Desktop.Pages.Appointments;

/// <summary>
/// One option in the booking panel's appointment-type grid, mirroring the
/// reference's <c>type-opt</c> button (icon box, name + duration subtitle and
/// the corner tick), with its pre-resolved per-type accent/background brushes
/// from <see cref="AppointmentsPresentation"/>.
/// </summary>
public sealed partial class BookingTypeOptionViewModel : ObservableObject
{
    public BookingTypeOptionViewModel(AppointmentType type, bool isSelected, int durationMinutes)
    {
        Type = type;
        _isSelected = isSelected;
        DurationText = AppointmentsPresentation.Fa(durationMinutes) + " دقیقه";
        AccentBrush = AppointmentsPresentation.TypeAccent(type);
        BackgroundBrush = AppointmentsPresentation.TypeBackground(type);
        ForegroundBrush = AppointmentsPresentation.TypeForeground(type);
    }

    public AppointmentType Type { get; }

    public string Id => Type.Id;
    public string Label => Type.Label;
    public string IconKey => Type.IconKey;
    public string DurationText { get; }
    public IBrush AccentBrush { get; }
    public IBrush BackgroundBrush { get; }
    public IBrush ForegroundBrush { get; }

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>
/// One option in the booking panel's doctor grid, mirroring the reference's
/// <c>doc-opt</c> button: avatar initial, short name + specialty, the busy /
/// free-slot counters and the circular tick.
/// </summary>
public sealed partial class BookingDoctorOptionViewModel : ObservableObject
{
    public BookingDoctorOptionViewModel(AppointmentDoctor doctor, bool isSelected, int busyCount, int freeCount)
    {
        Doctor = doctor;
        _isSelected = isSelected;
        BusyText = AppointmentsPresentation.Fa(busyCount);
        FreeText = AppointmentsPresentation.Fa(freeCount) + " زمان آزاد";
        AccentBrush = AppointmentsPresentation.Accent(doctor.Accent);
    }

    public AppointmentDoctor Doctor { get; }

    public string Id => Doctor.Id;
    public string ShortName => Doctor.ShortName;
    public string Specialty => Doctor.Specialty;
    public string Initial => Doctor.Initial;
    public string BusyText { get; }
    public string FreeText { get; }
    public IBrush AccentBrush { get; }

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>
/// One row in the booking panel's patient pick-list, mirroring the reference's
/// <c>pat-opt</c> button (avatar, name + kind badge, owner · phone and the
/// selection tick) with its keyboard highlight state.
/// </summary>
public sealed partial class BookingPatientOptionViewModel : ObservableObject
{
    public BookingPatientOptionViewModel(BookingPatient patient, bool isSelected, bool isHighlighted)
    {
        Patient = patient;
        _isSelected = isSelected;
        _isHighlighted = isHighlighted;
        AvatarBrush = AppointmentsPresentation.AvatarBrush(patient.AvatarTone);
    }

    public BookingPatient Patient { get; }

    public string Id => Patient.Id;
    public string Name => Patient.Name;
    public string Kind => Patient.Kind;
    public string OwnerName => Patient.OwnerName;
    public string Phone => Patient.Phone;
    public string IconKey => Patient.SpeciesIconKey;
    public IBrush AvatarBrush { get; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isHighlighted;
}

/// <summary>
/// One species filter chip above the patient pick-list, mirroring the
/// reference's <c>mini-chip</c> (همه / سگ / گربه / پرنده / سایر).
/// </summary>
public sealed partial class BookingKindChipViewModel : ObservableObject
{
    public BookingKindChipViewModel(string id, string label, bool isSelected)
    {
        Id = id;
        Label = label;
        _isSelected = isSelected;
    }

    public string Id { get; }
    public string Label { get; }

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>
/// One duration option above the slot grid, mirroring the reference's
/// <c>dur</c> button (15/20/30/45/60/90 minutes).
/// </summary>
public sealed partial class BookingDurationOptionViewModel : ObservableObject
{
    public BookingDurationOptionViewModel(int minutes, bool isSelected)
    {
        Minutes = minutes;
        Label = AppointmentsPresentation.Fa(minutes) + " دقیقه";
        _isSelected = isSelected;
    }

    public int Minutes { get; }
    public string Label { get; }

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>
/// One day cell in the booking panel's mini calendar, mirroring the reference's
/// <c>cal-d</c> button: the Persian day number, the current-month/out flag, the
/// today/selected/Friday states and the appointment-count dot.
/// </summary>
public sealed class BookingCalendarDayViewModel
{
    public BookingCalendarDayViewModel(
        DateOnly day, bool isOut, bool isSelected, bool isToday, bool isOffDay, int count)
    {
        Day = day;
        DayKey = day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        IsOut = isOut;
        IsSelected = isSelected;
        IsToday = isToday;
        IsOffDay = isOffDay;
        Count = count;
        NumberText = AppointmentsCalendarMath.DayNumber(day);
        HasDot = count > 0 && !isOut;
        IsHighCount = count > 5;
        TooltipText = $"{AppointmentsCalendarMath.DayNames[AppointmentsCalendarMath.WeekIndex(day)]} " +
                      $"{AppointmentsCalendarMath.Full(day)} · {AppointmentsPresentation.Fa(count)} نوبت";
    }

    public DateOnly Day { get; }
    public string DayKey { get; }
    public bool IsOut { get; }
    public bool IsSelected { get; }
    public bool IsToday { get; }
    public bool IsOffDay { get; }
    public int Count { get; }
    public string NumberText { get; }
    public bool HasDot { get; }
    public bool IsHighCount { get; }
    public string TooltipText { get; }
}

/// <summary>
/// One start-time button in the booking panel's slot grid, mirroring the
/// reference's <c>slot</c> button (booked slots render disabled with a
/// strike-through).
/// </summary>
public sealed class BookingSlotViewModel
{
    public BookingSlotViewModel(TimeSlot slot, bool isSelected)
    {
        Minutes = slot.Minutes;
        IsFree = slot.IsFree;
        IsSelected = isSelected;
        TimeText = AppointmentsPresentation.Time(slot.Minutes);
    }

    public int Minutes { get; }
    public bool IsFree { get; }
    public bool IsSelected { get; }
    public string TimeText { get; }
}

/// <summary>
/// One morning/noon/afternoon/evening slot band (صبح/ظهر/عصر/شب), carrying its
/// label and the slots that fall in it.
/// </summary>
public sealed class BookingSlotGroupViewModel
{
    public BookingSlotGroupViewModel(string label, IReadOnlyList<BookingSlotViewModel> slots)
    {
        Label = label;
        Slots = slots;
    }

    public string Label { get; }
    public IReadOnlyList<BookingSlotViewModel> Slots { get; }
}

/// <summary>
/// One key/value row in the booking panel's running summary sidebar
/// (the reference's <c>sum-rows &gt; div</c>).
/// </summary>
public sealed class BookingSummaryRowViewModel
{
    public BookingSummaryRowViewModel(string key, string value, bool isTag = false)
    {
        Key = key;
        Value = value;
        IsTag = isTag;
    }

    public string Key { get; }
    public string Value { get; }
    public bool IsTag { get; }
}
