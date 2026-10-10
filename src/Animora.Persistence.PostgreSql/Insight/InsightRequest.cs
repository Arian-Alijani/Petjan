namespace Animora.Persistence.PostgreSql.Insight;
public sealed record InsightRequest(string Metric, string? TenantId, DateOnly From, DateOnly To, int Page, int PageSize);
