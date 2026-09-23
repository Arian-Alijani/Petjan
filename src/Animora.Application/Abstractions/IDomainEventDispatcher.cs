using Animora.Domain.Events;
using Animora.Domain.Results;
namespace Animora.Application.Abstractions;
public interface IDomainEventDispatcher
{
    Task<Result> DispatchAsync(DomainEvent domainEvent, CancellationToken ct = default);
}
public interface IDomainEventHandler<in TEvent> where TEvent : DomainEvent
{
    Task<Result> HandleAsync(TEvent e, CancellationToken ct);
}
