namespace Animora.Persistence.PostgreSql.Insight;
public sealed record InsightPage<T>(IReadOnlyList<T> Items, int Page, int PageSize, bool Truncated);
