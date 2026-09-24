using Animora.Domain.Results;

namespace Animora.Application.Outbox;

/// <summary>
/// The transactional outbox (ARCHITECTURE.md §12.1). A domain event and the business state change that
/// produced it commit in <b>one</b> transaction; a background relay then dispatches at-least-once.
/// </summary>
public interface IOutbox
{
    /// <summary>
    /// Appends a message to the outbox in the current transaction — <b>never</b> calls <c>SaveChanges</c>, so
    /// the write rides the caller's business transaction (§12.1).
    /// </summary>
    Task<Result> EnqueueAsync(string messageType, object payload, CancellationToken ct = default);
}
