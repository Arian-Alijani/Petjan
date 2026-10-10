using Animora.Domain.Results;
namespace Animora.Persistence.PostgreSql.Insight;
public interface IInsightMetricProvider
{
    string Name { get; }
    Task<Result<IReadOnlyList<InsightRow>>> ExecuteAsync(InsightQueryContext ctx, CancellationToken ct = default);
}
