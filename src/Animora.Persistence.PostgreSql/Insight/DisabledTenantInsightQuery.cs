using Animora.Domain.Results;
namespace Animora.Persistence.PostgreSql.Insight;
public sealed class DisabledTenantInsightQuery : ITenantInsightQuery
{
    public bool IsEnabled => false;
    public Task<Result<InsightPage<InsightRow>>> QueryAsync(InsightRequest request, CancellationToken ct = default)
        => Task.FromResult(Result<InsightPage<InsightRow>>.Fail(ErrorCodes.InsightNotEnabled));
}
