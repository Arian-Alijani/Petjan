using System.Collections.Frozen;
namespace Animora.Persistence.PostgreSql.Insight;
public static class InsightMetrics
{
    // Immutable and deliberately empty: enabling the seam registers no analytics feature.
    public static IReadOnlySet<string> Registered { get; } = Array.Empty<string>().ToFrozenSet(StringComparer.Ordinal);
}
