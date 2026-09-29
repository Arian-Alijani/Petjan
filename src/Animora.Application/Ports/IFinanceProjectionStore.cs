using Animora.Domain.Events;

namespace Animora.Application.Ports;

/// <summary>Stages rebuildable finance facts in the current outbox completion transaction.</summary>
public interface IFinanceProjectionStore
{
    Task ApplyAsync(DomainEvent domainEvent, int localDay, CancellationToken ct);
}
