using Animora.Domain.Primitives;

namespace Animora.Persistence.Sqlite.Entities;

/// <summary>
/// The single-row monotonic counter behind <c>LocalSequenceAllocator</c> (ARCHITECTURE.md §8.3): every command
/// envelope carries a <c>LocalSequence</c> that is unique and increasing per installation. Never replicated.
/// </summary>
[LocalOnly]
public sealed class LocalSequenceCounter
{
    /// <summary>Stable key of the single row.</summary>
    public const string DefaultName = "command";

    private LocalSequenceCounter()
    {
    }

    public string Name { get; private set; } = DefaultName;
    public long Value { get; private set; }
}
