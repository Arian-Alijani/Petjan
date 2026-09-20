using Animora.Contracts.Visits;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Avalonia.Media;

namespace Animora.Desktop.Pages.Visits;

/// <summary>
/// Adapts a <see cref="BookedAppointmentRef"/> to one row of a doctor's booked
/// list, mirroring the reference's <c>apptRowHtml</c> (time + duration chip,
/// pet avatar, patient/owner/type/state line, and the «شروع ویزیت» CTA).
/// </summary>
public sealed class BookedRowViewModel
{
    public BookedRowViewModel(BookedAppointmentRef appointment, VisitTypeInfo type)
    {
        Appointment = appointment;
        TicketId = appointment.TicketId;

        var hours = appointment.AtMinutes / 60;
        var minutes = appointment.AtMinutes % 60;
        TimeText = PersianNumberFormatter.ToPersianDigits($"{hours:00}:{minutes:00}");
        DurationText = $"{PersianNumberFormatter.ToPersianDigits(appointment.DurationMinutes)} دقیقه";

        PatientName = appointment.PatientName;
        PatientKind = appointment.PatientKind;
        OwnerName = appointment.OwnerName;
        OwnerPhone = appointment.OwnerPhone;

        PetIconKey = VisitsPresentation.PetIconKey(appointment.PatientIconKey);
        var (bg, fg) = VisitsPresentation.PetAvatar(appointment.PatientIconKey);
        PetAvatarBackground = bg;
        PetAvatarForeground = fg;

        TypeLabel = type.Label;
        TypeAccent = VisitsPresentation.Fill(type.Accent);

        IsUrgent = appointment.IsUrgent;
        StateText = appointment.State switch
        {
            BookedAppointmentState.Called => "فراخوانده شده",
            BookedAppointmentState.InVisit => "در ویزیت",
            _ => "آمادهٔ ویزیت",
        };
        StateTone = appointment.State switch
        {
            BookedAppointmentState.Called => Tone.Info,
            BookedAppointmentState.InVisit => Tone.Warn,
            _ => Tone.Ok,
        };
    }

    public BookedAppointmentRef Appointment { get; }
    public string TicketId { get; }
    public string TimeText { get; }
    public string DurationText { get; }
    public string PatientName { get; }
    public string PatientKind { get; }
    public string OwnerName { get; }
    public string OwnerPhone { get; }
    public string PetIconKey { get; }
    public IBrush PetAvatarBackground { get; }
    public IBrush PetAvatarForeground { get; }
    public string TypeLabel { get; }
    public IBrush TypeAccent { get; }
    public bool IsUrgent { get; }
    public string StateText { get; }
    public Tone StateTone { get; }

    /// <summary>Row border/background, mirroring <c>.appt-row(.urgent)</c>.</summary>
    public IBrush RowBackground => IsUrgent
        ? VisitsPresentation.Vertical("#fef4f2", "#ffffff")
        : BrushCache.Solid("#ffffff");

    public IBrush RowBorder => IsUrgent
        ? VisitsPresentation.FillAlpha("#d0463a", 0x59)
        : VisitsPresentation.Fill("#e5eaee");

    public IBrush TimeBackground => IsUrgent
        ? VisitsPresentation.Fill("#fef3f2")
        : VisitsPresentation.Fill("#f0fdfa");

    public IBrush TimeForeground => IsUrgent
        ? VisitsPresentation.Fill("#d92d20")
        : VisitsPresentation.Fill("#0e7566");

    public IBrush CtaBackground => IsUrgent
        ? VisitsPresentation.Fill("#d92d20")
        : VisitsPresentation.Fill("#0d9488");
}
