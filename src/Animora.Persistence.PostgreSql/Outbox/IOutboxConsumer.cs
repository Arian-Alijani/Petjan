using Animora.Domain.Results;
namespace Animora.Persistence.PostgreSql.Outbox;

public interface IOutboxConsumer
{
    string MessageType { get; }
    string Name { get; }
    Task<Result> ConsumeAsync(OutboxMessage message, CancellationToken ct);
}
