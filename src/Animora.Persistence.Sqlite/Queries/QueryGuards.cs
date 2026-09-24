namespace Animora.Persistence.Sqlite.Queries;

internal static class QueryGuards
{
    /// <summary>Validates a bounded, non-empty id set and returns it distinct.</summary>
    public static string[] Ids(IReadOnlyCollection<string> ids, int max, string name)
    {
        ArgumentNullException.ThrowIfNull(ids, name);
        var distinct = ids.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToArray();
        if (distinct.Length == 0 || distinct.Length > max)
            throw new ArgumentOutOfRangeException(name, distinct.Length, $"Expected 1..{max} distinct ids.");
        return distinct;
    }
}
