using Animora.Contracts.Reception;

namespace Animora.Desktop.Mocks;

public sealed class MockReceptionQueueService : IReceptionQueueService
{
    private readonly object _gate = new();
    private readonly List<QueueTicket> _queue;

    public MockReceptionQueueService()
    {
        var now = DateTimeOffset.Now;
        _queue =
        [
            Ticket("qt-101", "پشمک", "گربه", "مریم محمدی", "۰۹۱۲۳۴۵۶۷۸۹", "dr-nouri", "دکتر نوری", "داخلی", "ن", "#0d9488", QueueTicketSource.Urgent, QueueTicketState.Waiting, true, now.AddMinutes(-36), 30, 1),
            Ticket("qt-102", "کوپر", "سگ", "علی رضایی", "۰۹۱۹۸۷۶۵۴۳۲", "dr-azadi", "دکتر آزادی", "جراحی", "آ", "#1570cd", QueueTicketSource.Appointment, QueueTicketState.Called, false, now.AddMinutes(-27), 30, 2),
            Ticket("qt-103", "لونا", "گربه", "سارا احمدی", "۰۹۳۵۱۲۳۴۵۶۷", "dr-nouri", "دکتر نوری", "داخلی", "ن", "#0d9488", QueueTicketSource.WalkIn, QueueTicketState.Waiting, false, now.AddMinutes(-19), 30, 3),
            Ticket("qt-104", "رکس", "سگ", "رضا کریمی", "۰۹۱۰۲۲۲۳۳۴۴", "dr-kiani", "دکتر کیانی", "پوست و مو", "ک", "#a48afb", QueueTicketSource.Appointment, QueueTicketState.InVisit, false, now.AddMinutes(-52), 45, 4),
            Ticket("qt-105", "طوطی", "پرنده", "نگار رستمی", "۰۹۹۰۱۲۳۴۵۶۷", "dr-azadi", "دکتر آزادی", "جراحی", "آ", "#1570cd", QueueTicketSource.WalkIn, QueueTicketState.Done, false, now.AddMinutes(-91), 30, 5),
            Ticket("qt-106", "بانی", "خرگوش", "امیر حسینی", "۰۹۱۲۱۱۱۲۲۳۳", "dr-kiani", "دکتر کیانی", "پوست و مو", "ک", "#a48afb", QueueTicketSource.Appointment, QueueTicketState.NoShow, false, now.AddMinutes(-74), 30, 6),
        ];
    }

    public Task<IReadOnlyList<QueueTicket>> GetQueueAsync()
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<QueueTicket>>(
                _queue.OrderByDescending(x => x.IsUrgent && IsActive(x.State))
                    .ThenBy(x => x.ArrivalTime)
                    .ToArray());
        }
    }

    public Task AdmitAsync(string ticketId) => MutateAsync(ticketId, ticket =>
        ticket.State == QueueTicketState.Waiting
            ? ticket with { State = QueueTicketState.Called }
            : ticket);

    public Task EnterVisitAsync(string ticketId) => MutateAsync(ticketId, ticket =>
        ticket.State is QueueTicketState.Waiting or QueueTicketState.Called
            ? ticket with { State = QueueTicketState.InVisit }
            : ticket);

    public Task LeaveQueueAsync(string ticketId) => MutateAsync(ticketId, ticket =>
        IsActive(ticket.State) ? ticket with { State = QueueTicketState.Left } : ticket);

    public Task MarkNoShowAsync(string ticketId) => MutateAsync(ticketId, ticket =>
        ticket.State is QueueTicketState.Waiting or QueueTicketState.Called
            ? ticket with { State = QueueTicketState.NoShow }
            : ticket);

    public Task ToggleUrgentAsync(string ticketId) => MutateAsync(ticketId, ticket =>
        ticket with { IsUrgent = !ticket.IsUrgent });

    public Task<QueueTicket> CreateWalkInAsync(WalkInRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (_gate)
        {
            var doctor = Doctors.FirstOrDefault(x => x.Id == request.DoctorId) ?? Doctors[0];
            var ticket = Ticket(
                $"qt-{Guid.NewGuid():N}",
                string.IsNullOrWhiteSpace(request.Name) ? "بدون‌نام" : request.Name.Trim(),
                "—",
                string.IsNullOrWhiteSpace(request.Owner) ? "—" : request.Owner.Trim(),
                string.IsNullOrWhiteSpace(request.Phone) ? "—" : request.Phone.Trim(),
                doctor.Id,
                doctor.Name,
                doctor.Specialty,
                doctor.Name.Replace("دکتر ", string.Empty)[..1],
                doctor.Id == "dr-nouri" ? "#0d9488" : doctor.Id == "dr-azadi" ? "#1570cd" : "#a48afb",
                request.IsUrgent ? QueueTicketSource.Urgent : QueueTicketSource.WalkIn,
                QueueTicketState.Waiting,
                request.IsUrgent,
                DateTimeOffset.Now,
                30,
                _queue.Count + 1);
            _queue.Add(ticket);
            return Task.FromResult(ticket);
        }
    }

    public static IReadOnlyList<ReceptionDoctor> Doctors { get; } =
    [
        new("dr-nouri", "دکتر نوری", "داخلی"),
        new("dr-azadi", "دکتر آزادی", "جراحی"),
        new("dr-kiani", "دکتر کیانی", "پوست و مو"),
    ];

    private Task MutateAsync(string ticketId, Func<QueueTicket, QueueTicket> mutation)
    {
        lock (_gate)
        {
            var index = _queue.FindIndex(x => x.Id == ticketId);
            if (index < 0)
            {
                throw new KeyNotFoundException($"Queue ticket '{ticketId}' was not found.");
            }

            _queue[index] = mutation(_queue[index]);
        }

        return Task.CompletedTask;
    }

    private static bool IsActive(QueueTicketState state) =>
        state is QueueTicketState.Waiting or QueueTicketState.Called or QueueTicketState.InVisit;

    private static QueueTicket Ticket(
        string id,
        string patient,
        string kind,
        string owner,
        string phone,
        string doctorId,
        string doctor,
        string specialty,
        string initials,
        string accent,
        QueueTicketSource source,
        QueueTicketState state,
        bool urgent,
        DateTimeOffset arrival,
        int duration,
        int sequence) =>
        new(id, patient, kind, owner, phone, doctorId, doctor, specialty, initials, accent, source, state, urgent, arrival, duration, sequence);
}
