using Animora.Domain.Scheduling;

namespace Animora.Application.Ports;

public interface IReceptionQueueRepository
{
    Task<ReceptionQueueEntry?> FindAsync(string id, CancellationToken ct = default);
    Task<ReceptionQueueEntry> RequireAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// The (tracked) queue row lazily created for an appointment-sourced ticket, or
    /// <see langword="null"/> when the appointment has not yet triggered a state change (C02).
    /// </summary>
    Task<ReceptionQueueEntry?> FindByAppointmentAsync(string appointmentId, CancellationToken ct = default);
    Task<ReceptionQueueEntry?> FindByVisitAsync(string visitId, CancellationToken ct = default);

    /// <summary>
    /// How many queue rows were enqueued in <c>[startUtc, endUtc)</c> — the per-local-day ticket-number
    /// counter for the next walk-in/check-in (C02). Range is on <c>EnqueuedUtc</c>, never a date function.
    /// </summary>
    Task<int> CountForDayAsync(DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken ct = default);

    void Add(ReceptionQueueEntry entity);
    void AddRange(IEnumerable<ReceptionQueueEntry> entities);
}
