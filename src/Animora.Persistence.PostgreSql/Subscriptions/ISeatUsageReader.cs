namespace Animora.Persistence.PostgreSql.Subscriptions;

public interface ISeatUsageReader
{
    Task<int> CountAsync(string tenantId, CancellationToken ct = default);
}
