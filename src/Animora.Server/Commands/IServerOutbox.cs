using Animora.Contracts.Wire.Commands;
using Animora.Domain.Results;
using Animora.Persistence.PostgreSql.Outbox;
namespace Animora.Server.Commands;

/// <summary>Server facade preserves the S06 intake seam while delegating inward to persistence.</summary>
internal interface IServerOutbox : Animora.Persistence.PostgreSql.Outbox.IServerOutbox { }

internal sealed class CommandServerOutbox(ServerOutbox outbox) : IServerOutbox
{
    public Task<Result> AppendAsync(CommandEnvelope envelope, SettlementResult settlement, CancellationToken ct = default) =>
        outbox.AppendAsync(envelope, settlement, ct);
}
