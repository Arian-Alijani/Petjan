using Animora.Contracts.Wire.Commands;
using Animora.Domain.Results;

namespace Animora.Server.Commands;

/// <summary>S13 replaces this seam. Called only in the transaction carrying the accepted effect.</summary>
internal interface IServerOutbox
{
    Task<Result> AppendAsync(CommandEnvelope envelope, SettlementResult settlement, CancellationToken ct = default);
}

internal sealed class NoServerOutbox : IServerOutbox
{
    public Task<Result> AppendAsync(CommandEnvelope envelope, SettlementResult settlement, CancellationToken ct = default) =>
        Task.FromResult(Result.Ok());
}
