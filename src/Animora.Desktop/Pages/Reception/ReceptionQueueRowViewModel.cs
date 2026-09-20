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

    // Exactly four possible backgrounds for a row (urgent / called / inVisit /
    // normal), each a vertical top-colour→white wash with the white stop at
    // 75% (matching the original design). GradientBrushCache's two-stop helper
    // always places the second stop at offset 1, so it cannot model this 0.75
    // stop; per PERFORMANCE.md §10/§15 these four combinations are built once
    // as static, immutable, shared instances instead of per-row allocation.
    private static readonly IBrush UrgentBackground = BuildBackground("#fff7f4");
    private static readonly IBrush CalledBackground = BuildBackground("#f1fbf7");
    private static readonly IBrush InVisitBackground = BuildBackground("#f0fdfa");
    private static readonly IBrush NormalBackground = BuildBackground("#ffffff");

    private static IBrush BuildBackground(string topHex)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.TryParse(topHex, out var top) ? top : Colors.Transparent, 0),
                new GradientStop(Colors.White, 0.75),
            },
        };
        return brush.ToImmutable();
    }

    private static IBrush ResolveBackground(QueueTicket ticket)
    {
        if (ticket.IsUrgent)
        {
            return UrgentBackground;
        }

        return ticket.State switch
        {
            QueueTicketState.Called => CalledBackground,
            QueueTicketState.InVisit => InVisitBackground,
            _ => NormalBackground,
        };
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
