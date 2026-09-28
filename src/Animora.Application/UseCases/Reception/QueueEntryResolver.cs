using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Domain.Results;
using Animora.Domain.Scheduling;
using Animora.Domain.Time;

namespace Animora.Application.UseCases.Reception;

/// <summary>Shared idempotent appointment queue creation for reception and booking check-in.</summary>
internal static class QueueEntryResolver
{
    internal static async Task<Result<ReceptionQueueEntry>> ResolveOrCreateForAppointmentAsync(
        Appointment appointment, CommandContext context, IReceptionQueueRepository queue,
        IClock clock, CancellationToken ct)
    {
        var existing = await queue.FindByAppointmentAsync(appointment.Id, ct);
        if (existing is not null) return Result<ReceptionQueueEntry>.Ok(existing);
        var number = await NextTicketNumberAsync(queue, clock, ct);
        var created = ReceptionQueueEntry.Create(context.TenantId, appointment.PetId, appointment.OwnerId,
            QueueSource.Appointment, number, clock, appointmentId: appointment.Id,
            priority: ReceptionQueueEntry.PriorityNormal);
        queue.Add(created);
        return Result<ReceptionQueueEntry>.Ok(created);
    }

    internal static async Task<int> NextTicketNumberAsync(IReceptionQueueRepository queue, IClock clock, CancellationToken ct)
    {
        var today = clock.UtcNow.UtcDateTime.Date;
        var startUtc = new DateTimeOffset(today, TimeSpan.Zero);
        return 1 + await queue.CountForDayAsync(startUtc, startUtc.AddDays(1), ct);
    }
}
