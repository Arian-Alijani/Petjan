using Animora.Domain.Results;
using Animora.Persistence.PostgreSql.Licensing;
using Animora.Persistence.PostgreSql.Outbox;
namespace Animora.Server.Licensing;

internal sealed class LicenseRecheckConsumer(LicenseGrantProjector projector) : IOutboxConsumer
{
    public string MessageType => OutboxMessageTypes.LicenseRecheck;
    public string Name => "license-recheck";
    public Task<Result> ConsumeAsync(OutboxMessage message, CancellationToken ct) => projector.ProjectAsync(message.TenantId, ct);
}
