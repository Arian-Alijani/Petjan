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
        // Shared, cached solid brush (PERFORMANCE.md §10) — the doctor accents
        // are a fixed palette, so no per-row allocation.
        DoctorBrush = BrushCache.Solid(ticket.DoctorAccent);
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
        // Shared via GradientBrushCache (PERFORMANCE.md §10): each state's top
        // stop is one of a fixed, handful of palette colours fading to white.
        // The reference fades to white at 75%; the cache's two-stop key models
        // the endpoints, so this keeps the same visible top→white wash with a
        // bounded key space.
        var top = ticket.IsUrgent
            ? "#fff7f4"
            : ticket.State switch
            {
                QueueTicketState.Called => "#f1fbf7",
                QueueTicketState.InVisit => "#f0fdfa",
                _ => "#ffffff",
            };
        return GradientBrushCache.LinearGradient(top, "#ffffff", 0, 0, 0, 1);
    }

    private static IBrush ResolveBorder(QueueTicket ticket) => BrushCache.Solid(
        ticket.IsUrgent
            ? "#f5c7c1"
            : ticket.State == QueueTicketState.InVisit
                ? "#2dd4bf"
                : ticket.State == QueueTicketState.Called
                    ? "#ccfbf1"
                    : "#e5eaee");
}
