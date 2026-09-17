using Animora.Contracts.Reception;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Avalonia;
using Avalonia.Media;

namespace Animora.Desktop.Pages.Reception;

public sealed class ReceptionQueueRowViewModel
{
    public ReceptionQueueRowViewModel(QueueTicket ticket, int displaySequence)
    {
        Ticket = ticket;
        SequenceText = PersianNumberFormatter.ToPersianDigits(displaySequence);
        ArrivalText = PersianNumberFormatter.ToPersianDigits(ticket.ArrivalTime.ToString("HH:mm"));
        DurationText = $"{PersianNumberFormatter.ToPersianDigits(ticket.DurationMinutes)} دقیقه";
        PriorityText = ticket.IsUrgent ? "اورژانس" : "عادی";
        PriorityTone = ticket.IsUrgent ? Tone.Danger : Tone.Ok;
        SourceText = ticket.Source switch
        {
            QueueTicketSource.Appointment => "نوبت از پیش",
            QueueTicketSource.Urgent => "اورژانس",
            _ => "حضوری",
        };
        StateText = ticket.State switch
        {
            QueueTicketState.Waiting => "در انتظار",
            QueueTicketState.Called => "فراخوانده",
            QueueTicketState.InVisit => "در ویزیت",
            QueueTicketState.Done => "پایان‌یافته",
            QueueTicketState.Left => "ترک صف",
            QueueTicketState.NoShow => "عدم مراجعه",
            QueueTicketState.Cancelled => "لغو",
            _ => string.Empty,
        };
        StateTone = ticket.State switch
        {
            QueueTicketState.Waiting => Tone.Info,
            QueueTicketState.Called => Tone.Warn,
            QueueTicketState.InVisit or QueueTicketState.Done => Tone.Ok,
            QueueTicketState.NoShow => Tone.Danger,
            _ => Tone.Neutral,
        };
        RowBackground = ResolveBackground(ticket);
        RowBorder = ResolveBorder(ticket);
        DoctorBrush = new SolidColorBrush(Color.Parse(ticket.DoctorAccent));
    }

    public QueueTicket Ticket { get; }
    public string SequenceText { get; }
    public string ArrivalText { get; }
    public string DurationText { get; }
    public string PriorityText { get; }
    public Tone PriorityTone { get; }
    public string SourceText { get; }
    public Tone SourceTone => Ticket.Source == QueueTicketSource.Urgent ? Tone.Danger : Tone.Info;
    public string StateText { get; }
    public Tone StateTone { get; }
    public IBrush RowBackground { get; }
    public IBrush RowBorder { get; }
    public IBrush DoctorBrush { get; }
    public bool CanCallDoctor => Ticket.State == QueueTicketState.Waiting;
    public bool CanEnterVisit => Ticket.State is QueueTicketState.Waiting or QueueTicketState.Called;
    public bool CanAdmit => Ticket.State == QueueTicketState.Waiting;
    public bool CanViewEncounter => Ticket.State == QueueTicketState.InVisit;
    public bool CanRemove => true;
    public string UrgencyActionText => Ticket.IsUrgent ? "عادی" : "اورژانس";

    private static IBrush ResolveBackground(QueueTicket ticket)
    {
        if (ticket.IsUrgent)
        {
            return new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.Parse("#fff7f4"), 0),
                    new GradientStop(Colors.White, 0.75),
                },
            };
        }

        var top = ticket.State switch
        {
            QueueTicketState.Called => Color.Parse("#f1fbf7"),
            QueueTicketState.InVisit => Color.Parse("#f0fdfa"),
            _ => Colors.White,
        };
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(top, 0),
                new GradientStop(Colors.White, 0.75),
            },
        };
    }

    private static IBrush ResolveBorder(QueueTicket ticket) => new SolidColorBrush(
        ticket.IsUrgent
            ? Color.Parse("#f5c7c1")
            : ticket.State == QueueTicketState.InVisit
                ? Color.Parse("#2dd4bf")
                : ticket.State == QueueTicketState.Called
                    ? Color.Parse("#ccfbf1")
                    : Color.Parse("#e5eaee"));
}
