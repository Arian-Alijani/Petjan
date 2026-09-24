using System.Collections.Frozen;

namespace Animora.Application.Dispatch;

/// <summary>
/// The reflection-free dispatch core (PERFORMANCE.md §3): a <see cref="FrozenDictionary{TKey,TValue}"/>
/// from request type to its pre-composed <see cref="HandlerEntry"/>. Built once by
/// <see cref="ApplicationRegistrationBuilder"/>; immutable afterwards.
/// </summary>
public sealed class HandlerRegistry
{
    private readonly FrozenDictionary<Type, HandlerEntry> _entries;

    internal HandlerRegistry(FrozenDictionary<Type, HandlerEntry> entries) => _entries = entries;

    /// <summary>All entries, keyed by request type.</summary>
    public IReadOnlyDictionary<Type, HandlerEntry> Entries => _entries;

    public bool TryGet(Type requestType, out HandlerEntry entry)
    {
        ArgumentNullException.ThrowIfNull(requestType);
        return _entries.TryGetValue(requestType, out entry!);
    }
}
