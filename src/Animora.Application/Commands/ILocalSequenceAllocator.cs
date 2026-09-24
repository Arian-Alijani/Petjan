using Animora.Domain.Results;

namespace Animora.Application.Commands;

/// <summary>
/// Allocates the monotonic per-installation <c>LocalSequence</c> stamped on every command envelope
/// (ARCHITECTURE.md §8.3). Implemented over the local store with a single atomic counter increment, so two
/// concurrent enqueues never share a sequence (CONVENTIONS §5).
/// </summary>
public interface ILocalSequenceAllocator
{
    /// <summary>Reserves the next sequence value for this installation.</summary>
    Task<Result<long>> NextAsync(CancellationToken ct = default);
}
