using Animora.Application.Abstractions;
using Animora.Application.Dispatch;
using Animora.Application.Security;
using Animora.Application.UseCases.Reception;
using Animora.Contracts.Reception;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Client.Services.Reception;

public sealed class ReceptionQueueService(IServiceScopeFactory scopes) : IReceptionQueueService
{
    public async Task<IReadOnlyList<QueueTicket>> GetQueueAsync()
    {
        await using var scope = scopes.CreateAsyncScope();
        var provider = scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>();
        var context = provider.BuildQueryContext();
        if (context.IsFailure) throw new ServiceException(context.Error);
        var today = await ClinicDayWindow.TodayDateAsync(scopes).ConfigureAwait(false); // INF-01: clinic-TZ day via IClock
        var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>()
            .QueryAsync(new ReceptionQueueQuery(today), context.Value!);
        if (result.IsFailure) throw new ServiceException(result.Error);
        return result.Value!.Select(x => x.ToContract()).ToArray();
    }

    public Task AdmitAsync(string ticketId) => SendAsync(new AdmitTicketCommand(ticketId));
    public Task EnterVisitAsync(string ticketId) => SendAsync(new EnterVisitCommand(ticketId));
    public Task LeaveQueueAsync(string ticketId) => SendAsync(new LeaveQueueCommand(ticketId));
    public Task MarkNoShowAsync(string ticketId) => SendAsync(new MarkNoShowCommand(ticketId));
    public Task ToggleUrgentAsync(string ticketId) => SendAsync(new ToggleTicketUrgentCommand(ticketId));

    public async Task<QueueTicket> CreateWalkInAsync(WalkInRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var ticketId = await SendAsync(new CreateWalkInCommand(request.Name, request.Owner, request.Phone,
            request.DoctorId, request.IsUrgent));
        return (await GetQueueAsync()).First(x => x.Id == ticketId);
    }

    private async Task SendAsync(ICommand<Unit> command) => _ = await SendAsync<Unit>(command);

    private async Task<T> SendAsync<T>(ICommand<T> command)
    {
        await using var scope = scopes.CreateAsyncScope();
        var provider = scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>();
        var context = await provider.BuildCommandContextAsync(null, null, null, default);
        if (context.IsFailure) throw new ServiceException(context.Error);
        var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(command, context.Value!);
        if (result.IsFailure) throw new ServiceException(result.Error);
        return result.Value!;
    }
}
