using Animora.Domain.Events;

namespace Animora.Domain.Primitives;

/// <summary>
/// A replicated aggregate root: the syncable shape plus a domain-event buffer.
/// Only aggregate roots raise events; child entities mutate through their root.
/// </summary>
public abstract class AggregateRoot : SyncableEntity, IHasDomainEvents
{
    private readonly List<DomainEvent> _events = [];

    /// <summary>EF Core materialization constructor. Not for business code.</summary>
    protected AggregateRoot()
    {
    }

    /// <inheritdoc />
    public IReadOnlyList<DomainEvent> DomainEvents => _events;

    /// <inheritdoc />
    public void ClearDomainEvents() => _events.Clear();

    /// <summary>Buffers a domain event for dispatch by the unit of work.</summary>
    protected void Raise(DomainEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        _events.Add(e);
    }
}
