using Animora.Persistence.PostgreSql.Options;
namespace Animora.Persistence.PostgreSql.Insight;
// The session is never an OLTP transaction; future providers use only catalogued projections.
public sealed record InsightQueryContext(InsightRequest Request, ReportingQuerySession Session);
