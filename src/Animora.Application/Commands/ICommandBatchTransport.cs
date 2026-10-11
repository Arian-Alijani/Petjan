using Animora.Contracts.Wire.Commands;
using Animora.Domain.Results;

namespace Animora.Application.Commands;

/// <summary>
/// Optional batch optimisation of <see cref="ICommandTransport"/>: submits several durable commands in one round trip.
/// The result list is <b>ordered exactly like the request</b> (one <see cref="SettlementResult"/> per envelope), so the caller
/// can correlate by position as well as by <c>OperationId</c>. A batch is a transport optimisation only — every envelope
/// keeps its own idempotency key and a retry resubmits the original envelopes unchanged (ARCHITECTURE.md §12.3).
/// </summary>
public interface ICommandBatchTransport
{
    /// <summary>
    /// Submits <paramref name="envelopes"/> in one request. Fails with <c>BATCH_TOO_LARGE</c> when the transport's batch ceiling
    /// is exceeded (nothing is sent), otherwise returns one settlement per envelope in request order.
    /// </summary>
    Task<Result<IReadOnlyList<SettlementResult>>> SubmitBatchAsync(IReadOnlyList<CommandEnvelope> envelopes,
        CancellationToken ct = default);
}
