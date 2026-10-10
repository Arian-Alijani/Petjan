using System.Collections.Frozen;
namespace Animora.Persistence.PostgreSql.Insight;
public static class InsightSourceCatalog
{
    public static IReadOnlySet<string> AllowedTables { get; } = Array.Empty<string>().ToFrozenSet(StringComparer.Ordinal);
}
