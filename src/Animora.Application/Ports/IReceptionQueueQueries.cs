using Animora.Application.UseCases.Reception.ReadModels;

namespace Animora.Application.Ports;

/// <summary>
/// Projection reads backing the reception queue screen (C02). The queue is a <b>derived</b> view:
/// <see cref="GetQueueAsync"/> is a single statement (today's appointments LEFT-JOINed to their
/// queue rows, UNION-ALL the walk-ins), with the day expressed as a UTC-ticks range — never a SQL
/// date function, never per-ticket round trips (CONVENTIONS §5, PERFORMANCE §6).
/// </summary>
public interface IReceptionQueueQueries
{
    /// <summary>Today's queue tickets whose arrival falls in <c>[startUtc, endUtc)</c>.</summary>
    Task<IReadOnlyList<QueueTicketReadModel>> GetQueueAsync(
        DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken ct);

    /// <summary>Active doctors available for walk-in assignment.</summary>
    Task<IReadOnlyList<ReceptionDoctorReadModel>> GetDoctorsAsync(CancellationToken ct);
}
