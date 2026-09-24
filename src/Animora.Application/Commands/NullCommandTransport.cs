using Animora.Contracts.Wire.Commands;
using Animora.Domain.Results;

namespace Animora.Application.Commands;

/// <summary>
/// Default transport used until E06 supplies the real one. It defers every command, so a full processor pass
/// leaves each command in <c>PendingSettlement</c> and the desktop stays fully functional offline.
/// </summary>
public sealed class NullCommandTransport : ICommandTransport
{
    public Task<Result<SettlementResult>> SubmitAsync(CommandEnvelope envelope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return Task.FromResult(Result<SettlementResult>.Ok(new SettlementResult(
            envelope.OperationId,
            SettlementOutcome.Deferred,
            ErrorCode: null,
            Detail: "No command transport configured (offline).",
            ServerEntityId: null,
            ServerVersion: null,
            SettledAtUtc: DateTimeOffset.UnixEpoch,
            FinancialEffect: FinancialEffect.Unknown,
            ResponsibleRole: null)));
    }
}
