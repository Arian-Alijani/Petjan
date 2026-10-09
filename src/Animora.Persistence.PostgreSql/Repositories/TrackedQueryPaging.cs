using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Repositories;

/// <summary>Complete write-side collection read in bounded pages (soft-delete cascades cannot drop pets).</summary>
internal static class TrackedQueryPaging
{
    internal static async Task<List<T>> ToTrackedListAsync<T>(this IOrderedQueryable<T> query, CancellationToken ct)
    {
        var result = new List<T>();
        for (var skip = 0; ; skip += PageRules.MaxPageSize)
        {
            var page = await query.Skip(skip).Take(PageRules.MaxPageSize).ToListAsync(ct).ConfigureAwait(false);
            result.AddRange(page);
            if (page.Count < PageRules.MaxPageSize) return result;
        }
    }
}
