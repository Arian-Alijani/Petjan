using Animora.Domain.Results;
using Animora.Persistence.PostgreSql.Licensing;
using Animora.Persistence.PostgreSql.Outbox;
namespace Animora.Server.Licensing;

internal sealed class LicenseIssueConsumer(LicenseGrantProjector projector) : IOutboxConsumer
{
    public string MessageType => OutboxMessageTypes.LicenseIssue;
    public string Name => "license-issue";
    public Task<Result> ConsumeAsync(OutboxMessage message, CancellationToken ct) => projector.ProjectAsync(message.TenantId, ct);
}
