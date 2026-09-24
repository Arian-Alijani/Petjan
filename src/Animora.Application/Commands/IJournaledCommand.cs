namespace Animora.Application.Commands;

/// <summary>
/// Implemented by a command that carries a real business intent and therefore must be journaled and settled
/// (ARCHITECTURE.md §8.3, CONVENTIONS §6 item 1). A command that does <b>not</b> implement this marker is a
/// purely local concern (UI preferences, profile settings) and never touches the journal — this is where
/// "replication ≠ intent" is enforced at the code level.
/// </summary>
public interface IJournaledCommand
{
    /// <summary>Catalog name from <c>CommandTypes</c>, e.g. <c>"sales.invoice.post"</c>.</summary>
    string CommandTypeName { get; }

    /// <summary>The aggregate this command targets, e.g. <c>"Invoice"</c>.</summary>
    string AggregateTypeName { get; }

    /// <summary>The target aggregate id, or <c>null</c> when the command creates a new aggregate.</summary>
    string? AggregateIdOrNull { get; }
}
