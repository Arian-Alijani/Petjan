using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Domain.Events;
using Animora.Domain.Results;

namespace Animora.Application.Activity;

/// <summary>Materializes the supported domain events alongside the outbox in the same UoW.</summary>
public sealed class ActivityFeedHandler(IActivityFeedWriter writer) : IDomainEventHandler<DomainEvent>
{
    public async Task<Result> HandleAsync(DomainEvent e, CancellationToken ct)
    {
        var draft = ActivityFeedMapper.Map(e);
        if (draft is not null) await writer.WriteAsync(draft, ct);
        return Result.Ok();
    }
}
