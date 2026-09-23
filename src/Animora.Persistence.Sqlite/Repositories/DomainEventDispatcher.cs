using Animora.Application.Abstractions;
using Animora.Domain.Events;
using Animora.Domain.Results;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Persistence.Sqlite.Repositories;

public sealed class DomainEventHandlerMap : Dictionary<Type, Func<IServiceProvider, IReadOnlyList<object>>>;

public sealed class DomainEventDispatcher(IServiceProvider services, DomainEventHandlerMap map) : IDomainEventDispatcher
{
    public async Task<Result> DispatchAsync(DomainEvent domainEvent, CancellationToken ct = default)
    {
        if (!map.TryGetValue(domainEvent.GetType(), out var factory)) return Result.Ok();
        foreach (var handler in factory(services))
        {
            // Map delegates are compiled by registrations, never discovered by scanning assemblies.
            var task = (Task<Result>)((dynamic)handler).HandleAsync((dynamic)domainEvent, ct);
            var result = await task;
            if (result.IsFailure) return result;
        }
        return Result.Ok();
    }
}
