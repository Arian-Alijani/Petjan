namespace Animora.Application.UseCases.Reception;

/// <summary>Discriminates how a reception queue ticket id was formed (C02).</summary>
public enum TicketKind
{
    /// <summary>Ticket projected from an <c>Appointment</c> — id is <c>"apt:" + AppointmentId</c>.</summary>
    Appointment = 0,

    /// <summary>Ticket backed by a walk-in <c>ReceptionQueueEntry</c> — id is <c>"wlk:" + QueueEntryId</c>.</summary>
    WalkIn = 1,
}

/// <summary>
/// The single parser/formatter for the Contracts <c>QueueTicket.Id</c> composite (C02, the core
/// decision). An appointment-sourced ticket is <c>"apt:" + AppointmentId</c>; a walk-in is
/// <c>"wlk:" + QueueEntryId</c>. Every <c>IReceptionQueueService</c> member that takes a
/// <c>ticketId</c> resolves it through <see cref="TryParse"/>; an unparsable value must surface as
/// <c>ErrorCodes.TicketNotFound</c>.
/// </summary>
public readonly record struct TicketRef(TicketKind Kind, string Id)
{
    private const string AppointmentPrefix = "apt:";
    private const string WalkInPrefix = "wlk:";

    /// <summary>Builds an appointment-sourced ticket reference.</summary>
    public static TicketRef FromAppointment(string appointmentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appointmentId);
        return new TicketRef(TicketKind.Appointment, appointmentId);
    }

    /// <summary>Builds a walk-in ticket reference.</summary>
    public static TicketRef FromWalkIn(string queueEntryId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueEntryId);
        return new TicketRef(TicketKind.WalkIn, queueEntryId);
    }

    /// <summary>The wire/Contracts composite id, e.g. <c>"apt:01H…"</c>.</summary>
    public string Format() => Kind switch
    {
        TicketKind.Appointment => AppointmentPrefix + Id,
        TicketKind.WalkIn => WalkInPrefix + Id,
        _ => Id,
    };

    /// <inheritdoc cref="Format"/>
    public override string ToString() => Format();

    /// <summary>
    /// Parses a Contracts <c>QueueTicket.Id</c>. Returns <see langword="false"/> for a null/blank
    /// value, an unknown prefix, or an empty id body — the caller then returns
    /// <c>ErrorCodes.TicketNotFound</c>.
    /// </summary>
    public static bool TryParse(string? value, out TicketRef ticket)
    {
        ticket = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.StartsWith(AppointmentPrefix, StringComparison.Ordinal))
        {
            var id = value[AppointmentPrefix.Length..];
            if (id.Length == 0)
            {
                return false;
            }

            ticket = new TicketRef(TicketKind.Appointment, id);
            return true;
        }

        if (value.StartsWith(WalkInPrefix, StringComparison.Ordinal))
        {
            var id = value[WalkInPrefix.Length..];
            if (id.Length == 0)
            {
                return false;
            }

            ticket = new TicketRef(TicketKind.WalkIn, id);
            return true;
        }

        return false;
    }
}
