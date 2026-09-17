namespace Animora.Contracts.Reception;

public interface IReceptionQueueService
{
    Task<IReadOnlyList<QueueTicket>> GetQueueAsync();

    Task AdmitAsync(string ticketId);

    Task EnterVisitAsync(string ticketId);

    Task LeaveQueueAsync(string ticketId);

    Task MarkNoShowAsync(string ticketId);

    Task ToggleUrgentAsync(string ticketId);

    Task<QueueTicket> CreateWalkInAsync(WalkInRequest request);
}
