using Animora.Domain.Results;
namespace Animora.Persistence.PostgreSql.Insight;
public interface ITenantInsightQuery
{
    bool IsEnabled { get; }
    Task<Result<InsightPage<InsightRow>>> QueryAsync(InsightRequest request, CancellationToken ct = default);
}
public interface IInsightOperatorContext { string OperatorId { get; } }
