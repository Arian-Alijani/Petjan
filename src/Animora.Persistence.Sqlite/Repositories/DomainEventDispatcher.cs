using Animora.Application.Abstractions;
using Animora.Domain.Events;
using Animora.Domain.Results;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Persistence.Sqlite.Repositories;

public delegate Task<Result> DomainEventHandlerInvoker(IServiceProvider services, DomainEvent domainEvent, CancellationToken ct);

/// <summary>One explicit registration made by <c>AddDomainEventHandler&lt;TEvent, THandler&gt;()</c>.</summary>
public sealed class DomainEventHandlerRegistration(Type eventType, Type handlerType, DomainEventHandlerInvoker invoke)
{
    public Type EventType { get; } = eventType;
    public Type HandlerType { get; } = handlerType;
    public DomainEventHandlerInvoker Invoke { get; } = invoke;

    public static DomainEventHandlerRegistration For<TEvent, THandler>()
        where TEvent : DomainEvent where THandler : class, IDomainEventHandler<TEvent> =>
        new(typeof(TEvent), typeof(THandler),
            (services, domainEvent, ct) => services.GetRequiredService<THandler>().HandleAsync((TEvent)domainEvent, ct));
}

/// <summary>Singleton map event type → handler invokers, in registration order. No scanning, no reflection invoke.</summary>
public sealed class DomainEventHandlerMap
{
    private readonly Dictionary<Type, DomainEventHandlerInvoker[]> _map;

    public DomainEventHandlerMap(IEnumerable<DomainEventHandlerRegistration> registrations) =>
        _map = registrations.GroupBy(r => r.EventType)
            .ToDictionary(g => g.Key, g => g.Select(r => r.Invoke).ToArray());

    public IReadOnlyList<DomainEventHandlerInvoker> HandlersFor(Type eventType) =>
        _map.TryGetValue(eventType, out var handlers) ? handlers : [];
}

/// <summary>Resolves handlers from the scoped provider; short-circuits on the first failure.</summary>
public sealed class DomainEventDispatcher(IServiceProvider services, DomainEventHandlerMap map) : IDomainEventDispatcher
{
    public async Task<Result> DispatchAsync(DomainEvent domainEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        // Walk the concrete type up to DomainEvent so a handler registered for the base event type (e.g. the
        // outbox relay, B02) also receives every derived event. Base handlers run after the exact-type ones.
        for (var type = domainEvent.GetType(); type is not null && typeof(DomainEvent).IsAssignableFrom(type);
             type = type.BaseType)
        {
            foreach (var handler in map.HandlersFor(type))
            {
                var result = await handler(services, domainEvent, ct);
                if (result.IsFailure) return result;
            }
        }
        return Result.Ok();
    }
}
