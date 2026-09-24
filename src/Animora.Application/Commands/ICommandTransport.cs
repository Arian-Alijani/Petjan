using Animora.Contracts.Wire.Commands;
using Animora.Domain.Results;

namespace Animora.Application.Commands;

/// <summary>
/// Submits a durable command to the settlement authority (the server). A port so the whole local pipeline is
/// testable and correct offline today: <see cref="NullCommandTransport"/> answers <c>Deferred</c> for
/// everything, and E06 supplies the real HTTP transport. A transport never retries by minting a new
/// <c>OperationId</c> — it resubmits the original envelope (ARCHITECTURE.md §12.3).
/// </summary>
public interface ICommandTransport
{
    Task<Result<SettlementResult>> SubmitAsync(CommandEnvelope envelope, CancellationToken ct = default);
}
