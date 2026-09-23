using Animora.Domain.Events;

namespace Animora.Domain.Primitives;

/// <summary>
/// Implemented by aggregates that buffer domain events until the unit of work
/// dispatches and clears them. The buffer is exposed read-only (CONVENTIONS.md §4).
/// </summary>
public interface IHasDomainEvents
{
    /// <summary>Events raised since the last <see cref="ClearDomainEvents"/>.</summary>
    IReadOnlyList<DomainEvent> DomainEvents { get; }

    /// <summary>Empties the buffer; called by the unit of work after dispatch.</summary>
    void ClearDomainEvents();
}
